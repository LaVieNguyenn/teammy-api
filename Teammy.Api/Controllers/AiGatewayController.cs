using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Teammy.Infrastructure.Ai;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/ai-gateway")]
public sealed class AiGatewayController(AiGatewayClient gateway, AppDbContext db) : ControllerBase
{
    [HttpGet("health")]
    //[Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<object>> HealthAsync(CancellationToken ct)
    {
        var result = await gateway.GetHealthAsync(ct);
        var payload = new
        {
            upstreamStatus = result.StatusCode,
            upstreamBody = result.Body
        };

        if (result.IsSuccess)
            return Ok(payload);

        return StatusCode(StatusCodes.Status502BadGateway, payload);
    }

    // =====================================================================
    // NEW: Draft generation FROM DB (Teammy builds payload -> AiGateway writes)
    // =====================================================================

    [HttpPost("generate-post/group/{groupId:guid}")]
    //[Authorize]
    public async Task<IActionResult> GenerateGroupPostDraftFromDbAsync(
        [FromRoute] Guid groupId,
        CancellationToken ct)
    {
        var row = await (from g in db.groups.AsNoTracking()
                         join t in db.topics.AsNoTracking() on g.topic_id equals t.topic_id into tj
                         from t in tj.DefaultIfEmpty()
                         where g.group_id == groupId
                         select new
                         {
                             Group = g,
                             TopicTitle = t != null ? t.title : null,
                             TopicDescription = t != null ? t.description : null,
                             TopicSkills = t != null ? t.skills : null
                         })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return NotFound(new { error = "Group not found" });

        var activeStatuses = new[] { "member", "leader" };

        var activeMemberIds = await db.group_members.AsNoTracking()
            .Where(m => m.group_id == groupId && activeStatuses.Contains(m.status))
            .Select(m => new { m.group_member_id, m.user_id })
            .ToListAsync(ct);

        var memberIds = activeMemberIds.Select(x => x.group_member_id).ToList();
        var rolesLookup = memberIds.Count == 0
            ? new Dictionary<Guid, string?>(0)
            : await db.group_member_roles.AsNoTracking()
                .Where(r => memberIds.Contains(r.group_member_id))
                .GroupBy(r => r.group_member_id)
                .Select(g => new
                {
                    GroupMemberId = g.Key,
                    Role = g.OrderByDescending(r => r.assigned_at).Select(r => r.role_name).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.GroupMemberId, x => x.Role, ct);

        (int mixFe, int mixBe, int mixOther) = (0, 0, 0);
        foreach (var m in activeMemberIds)
        {
            rolesLookup.TryGetValue(m.group_member_id, out var role);
            if (IsFrontendRole(role)) mixFe++;
            else if (IsBackendRole(role)) mixBe++;
            else mixOther++;
        }

        var remainingSlots = Math.Max(0, row.Group.max_members - activeMemberIds.Count);
        var neededRole = PickNeededRole(mixFe, mixBe, mixOther, remainingSlots);

        var openSlots = neededRole switch
        {
            "Frontend" => new AiGatewayMix(remainingSlots, 0, 0),
            "Backend" => new AiGatewayMix(0, remainingSlots, 0),
            _ => new AiGatewayMix(0, 0, remainingSlots)
        };

        var teamTopSkills = await BuildTeamTopSkillsAsync(
            groupSkillsRaw: row.Group.skills,
            topicSkillsRaw: row.TopicSkills,
            memberUserIds: activeMemberIds.Select(x => x.user_id).ToList(),
            ct);

        var preferRoles = new List<string> { neededRole };
        var avoidRoles = new List<string>();
        if (neededRole == "Frontend" && mixBe > mixFe) avoidRoles.Add("Backend");
        if (neededRole == "Backend" && mixFe > mixBe) avoidRoles.Add("Frontend");

        var project = !string.IsNullOrWhiteSpace(row.TopicTitle) || !string.IsNullOrWhiteSpace(row.TopicDescription)
            ? new AiGatewayProjectInfo(row.TopicTitle, row.TopicDescription)
            : null;

        var req = new AiGatewayGenerateGroupPostRequest(
            Group: new AiGatewayGroupInfo(
                Name: row.Group.name,
                PrimaryNeed: neededRole.ToLowerInvariant(),
                CurrentMix: new AiGatewayMix(mixFe, mixBe, mixOther),
                OpenSlots: openSlots,
                TeamTopSkills: teamTopSkills,
                PreferRoles: preferRoles,
                AvoidRoles: avoidRoles),
            Project: project,
            Options: new AiGatewayPostOptions(Language: "en", MaxWords: null, Tone: "friendly"));

        var result = await gateway.GenerateGroupPostDraftAsync(req, ct);

        if (result.IsSuccess)
            return Content(result.Body ?? "{}", "application/json", Encoding.UTF8);

        return StatusCode(StatusCodes.Status502BadGateway, new
        {
            upstreamStatus = result.StatusCode,
            upstreamBody = result.Body
        });
    }

    [HttpPost("generate-post/personal")]
    //[Authorize]
    public async Task<IActionResult> GeneratePersonalPostDraftFromDbAsync(
        [FromQuery] Guid? semesterId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        var user = await db.users.AsNoTracking()
            .Where(u => u.user_id == userId)
            .Select(u => new { u.user_id, u.display_name, u.skills, u.portfolio_url, u.desired_position_id })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return NotFound(new { error = "User not found" });

        var poolQuery =
            from p in db.mv_students_pools.AsNoTracking()
            join s in db.semesters.AsNoTracking() on p.semester_id equals s.semester_id
            where p.user_id == userId
            select new
            {
                p.skills,
                p.primary_role,
                p.desired_position_name,
                s.semester_id,
                s.is_active,
                s.start_date,
                s.year
            };

        if (semesterId.HasValue && semesterId.Value != Guid.Empty)
        {
            poolQuery = poolQuery.Where(x => x.semester_id == semesterId.Value);
        }
        else
        {
            poolQuery = poolQuery
                .Where(x => x.is_active)
                .OrderByDescending(x => x.start_date)
                .ThenByDescending(x => x.year);
        }

        var pool = await poolQuery
            .Select(x => new { x.skills, x.primary_role, x.desired_position_name })
            .FirstOrDefaultAsync(ct);

        var skills = ParseSkillsList(pool?.skills) ?? ParseSkillsList(user.skills) ?? new List<string>();
        skills = skills
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        var desiredPosition = pool?.desired_position_name;
        if (string.IsNullOrWhiteSpace(desiredPosition) && user.desired_position_id.HasValue)
        {
            desiredPosition = await db.position_lists.AsNoTracking()
                .Where(p => p.position_id == user.desired_position_id.Value)
                .Select(p => p.position_name)
                .FirstOrDefaultAsync(ct);
        }

        var inferredRole = pool?.primary_role;
        if (string.IsNullOrWhiteSpace(inferredRole))
            inferredRole = InferPrimaryRoleFromSkills(skills);

        var goal = "Join a project team";
        if (!string.IsNullOrWhiteSpace(inferredRole) && goal.Equals("Join a project team", StringComparison.OrdinalIgnoreCase))
            goal = $"Join a project team as {inferredRole}";

        // If portfolio exists and goal is short, keep as-is (AiGateway doesn't accept portfolio_url).

        var req = new AiGatewayGeneratePersonalPostRequest(
            User: new AiGatewayPersonalUser(
                DisplayName: user.display_name,
                DesiredPosition: desiredPosition,
                Skills: skills,
                Goal: goal,
                Availability: null),
            Options: new AiGatewayPostOptions(Language: "en", MaxWords: null, Tone: "professional"));

        var result = await gateway.GeneratePersonalPostDraftAsync(req, ct);

        if (result.IsSuccess)
            return Content(result.Body ?? "{}", "application/json", Encoding.UTF8);

        return StatusCode(StatusCodes.Status502BadGateway, new
        {
            upstreamStatus = result.StatusCode,
            upstreamBody = result.Body
        });
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("user_id");
        if (!Guid.TryParse(sub, out var id))
            throw new UnauthorizedAccessException("Invalid token");
        return id;
    }

    private static bool IsFrontendRole(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        return r.Contains("front") || r == "fe" || r.Contains("ui") || r.Contains("ux");
    }

    private static bool IsBackendRole(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        return r.Contains("back") || r == "be" || r.Contains("server") || r.Contains("api");
    }

    private static string PickNeededRole(int fe, int be, int other, int remainingSlots)
    {
        if (remainingSlots <= 0) return "Other";
        // Simple gap heuristic: if one side dominates, recruit the other side.
        if (be >= 2 && fe == 0) return "Frontend";
        if (fe >= 2 && be == 0) return "Backend";
        if (be > fe) return "Frontend";
        if (fe > be) return "Backend";
        return "Other";
    }

    private async Task<IReadOnlyList<string>> BuildTeamTopSkillsAsync(
        string? groupSkillsRaw,
        string? topicSkillsRaw,
        List<Guid> memberUserIds,
        CancellationToken ct)
    {
        var fromGroup = ParseSkillsList(groupSkillsRaw);
        if (fromGroup is { Count: > 0 })
            return fromGroup.Take(16).ToList();

        var fromTopic = ParseSkillsList(topicSkillsRaw);
        if (fromTopic is { Count: > 0 })
            return fromTopic.Take(16).ToList();

        if (memberUserIds.Count == 0)
            return Array.Empty<string>();

        var userSkills = await db.users.AsNoTracking()
            .Where(u => memberUserIds.Contains(u.user_id))
            .Select(u => u.skills)
            .ToListAsync(ct);

        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in userSkills)
        {
            var list = ParseSkillsList(raw);
            if (list is null) continue;
            foreach (var s in list)
            {
                var key = s.Trim();
                if (key.Length == 0) continue;
                freq.TryGetValue(key, out var n);
                freq[key] = n + 1;
            }
        }

        return freq
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => kv.Key)
            .Take(16)
            .ToList();
    }

    private static List<string>? ParseSkillsList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();

        // JSON array of strings: ["C#","Docker"]
        if (raw.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return doc.RootElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!.Trim())
                        .Where(x => x.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            catch
            {
                return null;
            }
        }

        // JSON object with "skills": [..]
        if (raw.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("skills", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    return arr.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!.Trim())
                        .Where(x => x.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            catch
            {
                return null;
            }
        }

        // CSV fallback: "aspnetcore, docker, sqlserver"
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string InferPrimaryRoleFromSkills(IReadOnlyList<string> skills)
    {
        var lower = skills.Select(s => s.ToLowerInvariant()).ToList();

        bool has(params string[] keys) => keys.Any(k => lower.Any(s => s.Contains(k)));

        if (has("react", "typescript", "tailwind", "next", "vue", "angular", "frontend")) return "Frontend";
        if (has("asp.net", "dotnet", "c#", "java", "spring", "node", "backend", "api")) return "Backend";
        if (has("flutter", "react native", "android", "ios")) return "Mobile";
        if (has("python", "pytorch", "llm", "ml", "ai")) return "AI";
        if (has("qa", "testing", "test")) return "QA";
        if (has("devops", "docker", "kubernetes", "ci", "cd")) return "DevOps";
        return "Other";
    }

}
