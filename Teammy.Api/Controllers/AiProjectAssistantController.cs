using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teammy.Application.Ai.Models;
using Teammy.Application.Ai.ProjectAssistant.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Kanban.Dtos;
using Teammy.Application.Kanban.Services;
using Teammy.Application.ProjectTracking.Dtos;
using Teammy.Application.ProjectTracking.Services;
using Teammy.Infrastructure.Ai;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/assistant")]
public sealed class AiProjectAssistantController(
    AiGatewayClient gateway,
    IAiLlmClient llm,
    IMemoryCache cache,
    AppDbContext db,
    ProjectTrackingService tracking,
    KanbanService board) : ControllerBase
{
    private sealed record AssistantConversationState(string DraftJson, DateTime UpdatedAtUtc);
    private sealed record MemberCandidate(
        Guid UserId,
        string DisplayName,
        string Email,
        string Role,
        string? AssignedRole,
        string? DesiredPosition,
        string? PrimaryRole,
        IReadOnlyList<string> SkillTags);
    private sealed record RecentTaskRow(
        Guid TaskId,
        string Title,
        string? Description,
        string? Status,
        string? Priority,
        DateTime? DueDate,
        DateTime UpdatedAt,
        Guid? BacklogItemId,
        Guid ColumnId,
        string ColumnName,
        bool ColumnIsDone);

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id)) throw new UnauthorizedAccessException("Invalid token");
        return id;
    }

    [HttpPost("draft")]
    [Authorize]
    public async Task<IActionResult> Draft(Guid groupId, [FromBody] ProjectAssistantDraftRequest req, CancellationToken ct)
    {
        var userId = GetUserId();

        var cacheKey = $"pm-assistant:draft:{groupId:N}:{userId:N}";
        cache.TryGetValue(cacheKey, out AssistantConversationState? last);

        var userMessage = (req.Message ?? "").Trim();
        if (string.IsNullOrWhiteSpace(userMessage))
            return BadRequest(new { error = "message is required" });

        // Read-only milestone questions should not create/update anything.
        // Handle them deterministically here (fast + accurate) instead of asking the LLM to guess an action.
        if (IsMilestoneSuitabilityQuestion(userMessage))
        {
            var boardVm = await board.GetBoardAsync(groupId, userId, status: null, page: null, pageSize: null, ct);
            var columns = boardVm.Columns.Select(c => new { columnId = c.ColumnId, name = c.ColumnName, isDone = c.IsDone }).ToList();

            var milestones = await tracking.ListMilestonesAsync(groupId, userId, ct);
            var milestoneCands = milestones
                .OrderBy(m => m.TargetDate ?? DateOnly.MaxValue)
                .Select(m => new { milestoneId = m.MilestoneId, name = m.Name, status = m.Status, targetDate = m.TargetDate })
                .ToList();

            var backlog = await tracking.ListBacklogAsync(groupId, userId, ct);
            var backlogCands = backlog
                .OrderByDescending(b => b.UpdatedAt)
                .Take(80)
                .Select(b => new { backlogItemId = b.BacklogItemId, title = b.Title, status = b.Status, dueDate = b.DueDate })
                .ToList();

            var recentTaskRows = await db.tasks.AsNoTracking()
                .Where(t => t.group_id == groupId)
                .OrderByDescending(t => t.updated_at)
                .Take(30)
                .Select(t => new RecentTaskRow(
                    t.task_id,
                    t.title,
                    t.description,
                    t.status,
                    t.priority,
                    t.due_date,
                    t.updated_at,
                    t.backlog_item_id,
                    t.column_id,
                    t.column.column_name,
                    t.column.is_done))
                .ToListAsync(ct);

            var taskCands = recentTaskRows
                .Take(60)
                .Select(t => new { taskId = t.TaskId, title = t.Title, status = t.Status, updatedAt = t.UpdatedAt, columnId = t.ColumnId, columnName = t.ColumnName })
                .ToList();

            var members = await BuildMemberCandidatesAsync(db, groupId, ct);

            string answerText;
            var questions = new List<string>();

            if (milestoneCands.Count == 0)
            {
                answerText = "I couldn’t find any milestones in this group yet.";
                questions.Add("Do you want to create a milestone for this feature? If yes, what milestone name and target date?");
            }
            else
            {
                var preview = milestoneCands
                    .Take(8)
                    .Select(m => m.targetDate is null
                        ? $"- {m.name} ({m.status})"
                        : $"- {m.name} ({m.status}, target {m.targetDate:yyyy-MM-dd})");

                answerText = "Here are the current milestones in this group:\n" + string.Join("\n", preview);
                questions.Add("Which milestone should we use for this feature (name or id)?");
            }

            var responseNode2 = new JsonObject
            {
                ["answerText"] = answerText,
                ["questions"] = new JsonArray(questions.Select(q => (JsonNode?)JsonValue.Create(q)).ToArray()),
                ["draft"] = null,
                ["dedupe"] = JsonSerializer.SerializeToNode(new { similarItems = Array.Empty<object>() }),
                ["candidates"] = JsonSerializer.SerializeToNode(new
                {
                    columns,
                    milestones = milestoneCands,
                    backlogItems = backlogCands,
                    tasks = taskCands,
                    members
                })
            };

            return Content(responseNode2.ToJsonString(), "application/json");
        }

        // If user is asking to edit/expand but doesn't mention which task, assume they refer to the last draft in this chat.
        // Important: do NOT prepend metadata to normal messages (it leaks into title/description).
        var effectiveMessage = userMessage;
        if (IsFollowUpEditRequest(userMessage) && last is not null)
        {
            effectiveMessage = $"""
You are continuing an existing Teammy work-item draft.

    TODAY_UTC: {DateTime.UtcNow:yyyy-MM-dd}

CURRENT_DRAFT_JSON:
{last.DraftJson}

USER_MESSAGE:
{userMessage}

    Update the draft accordingly.
    - Keep the title unless the user explicitly changes it.
    - If the existing actionType cannot represent the user's clarified intent (e.g. user wants a milestone but current payload has no milestone fields), switch to the closest allowed actionType and carry over all relevant fields.
    - If the user uses relative dates like "tomorrow", convert using TODAY_UTC.
    - IMPORTANT: TODAY_UTC and CURRENT_DRAFT_JSON are metadata. Do NOT copy them verbatim into any draft fields.
""";
        }

        var recentTasks = await db.tasks.AsNoTracking()
            .Where(t => t.group_id == groupId)
            .OrderByDescending(t => t.updated_at)
            .Take(30)
            .Select(t => new RecentTaskRow(
                t.task_id,
                t.title,
                t.description,
                t.status,
                t.priority,
                t.due_date,
                t.updated_at,
                t.backlog_item_id,
                t.column_id,
                t.column.column_name,
                t.column.is_done))
            .ToListAsync(ct);

        var similarItems = await ComputeSimilarTasksAsync(userMessage, recentTasks, ct);

        var upstream = await gateway.GenerateProjectAssistantDraftAsync(
            new AiGatewayPmAssistantDraftRequest(effectiveMessage),
            ct);

        if (!upstream.IsSuccess)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                upstreamStatus = upstream.StatusCode,
                upstreamBody = upstream.Body
            });
        }

        using var upstreamDoc = JsonDocument.Parse(upstream.Body ?? "{}");
        var merged = MergeDedupeIntoDraft(upstreamDoc.RootElement, similarItems);

        // Enrich response with candidates + basic name->id resolution for action-based drafts.
        var responseNode = JsonNode.Parse(merged.RootElement.GetRawText()) as JsonObject
                           ?? new JsonObject();

        if (responseNode["draft"] is JsonObject draftObj
            && draftObj["actionType"] is JsonValue
            && draftObj["actionPayload"] is JsonObject payloadObj)
        {
            var boardVm = await board.GetBoardAsync(groupId, userId, status: null, page: null, pageSize: null, ct);
            var columns = boardVm.Columns.Select(c => new { columnId = c.ColumnId, name = c.ColumnName, isDone = c.IsDone }).ToList();

            var milestones = await tracking.ListMilestonesAsync(groupId, userId, ct);
            var milestoneCands = milestones.Select(m => new { milestoneId = m.MilestoneId, name = m.Name, status = m.Status, targetDate = m.TargetDate }).ToList();

            var backlog = await tracking.ListBacklogAsync(groupId, userId, ct);
            var backlogCands = backlog
                .OrderByDescending(b => b.UpdatedAt)
                .Take(80)
                .Select(b => new { backlogItemId = b.BacklogItemId, title = b.Title, status = b.Status, dueDate = b.DueDate })
                .ToList();

            var taskCands = recentTasks
                .Take(60)
                .Select(t => new { taskId = t.TaskId, title = t.Title, status = t.Status, updatedAt = t.UpdatedAt, columnId = t.ColumnId, columnName = t.ColumnName })
                .ToList();

            var members = await BuildMemberCandidatesAsync(db, groupId, ct);

            responseNode["candidates"] = JsonSerializer.SerializeToNode(new
            {
                columns,
                milestones = milestoneCands,
                backlogItems = backlogCands,
                tasks = taskCands,
                members
            });

            static string Norm(string? s)
                => (s ?? "").Trim().ToLowerInvariant();

            static Guid? TryParseGuid(string? s)
                => Guid.TryParse(s, out var g) ? g : null;

            string? GetPayloadString(string name)
                => payloadObj[name]?.GetValue<string>();

            void SetPayloadGuidIfMissing(string idField, Guid? id)
            {
                if (payloadObj[idField] is not null) return;
                if (id.HasValue) payloadObj[idField] = id.Value.ToString();
            }

            void TryResolveColumn(string nameField, string idField)
            {
                if (payloadObj[idField] is not null) return;
                var name = Norm(GetPayloadString(nameField));
                if (string.IsNullOrWhiteSpace(name)) return;

                var match = columns.FirstOrDefault(c => Norm(c.name) == name)
                            ?? columns.FirstOrDefault(c => Norm(c.name).Contains(name) || name.Contains(Norm(c.name)));
                if (match is not null)
                    payloadObj[idField] = match.columnId.ToString();
            }

            void TryResolveMilestone(string nameField, string idField)
            {
                if (payloadObj[idField] is not null) return;
                var name = Norm(GetPayloadString(nameField));
                if (string.IsNullOrWhiteSpace(name)) return;

                var match = milestoneCands.FirstOrDefault(m => Norm(m.name) == name)
                            ?? milestoneCands.FirstOrDefault(m => Norm(m.name).Contains(name) || name.Contains(Norm(m.name)));
                if (match is not null)
                    payloadObj[idField] = match.milestoneId.ToString();
            }

            void TryResolveBacklog(string titleField, string idField)
            {
                if (payloadObj[idField] is not null) return;
                var title = Norm(GetPayloadString(titleField));
                if (string.IsNullOrWhiteSpace(title)) return;

                var match = backlogCands.FirstOrDefault(b => Norm(b.title) == title)
                            ?? backlogCands.FirstOrDefault(b => Norm(b.title).Contains(title) || title.Contains(Norm(b.title)));
                if (match is not null)
                    payloadObj[idField] = match.backlogItemId.ToString();
            }

            void TryResolveTask(string titleField, string idField)
            {
                if (payloadObj[idField] is not null) return;
                var title = Norm(GetPayloadString(titleField));
                if (string.IsNullOrWhiteSpace(title)) return;

                var exact = taskCands.Where(t => Norm(t.title) == title).ToList();
                if (exact.Count == 1)
                {
                    payloadObj[idField] = exact[0].taskId.ToString();
                    return;
                }

                var contains = taskCands.Where(t => Norm(t.title).Contains(title) || title.Contains(Norm(t.title))).ToList();
                if (contains.Count == 1)
                {
                    payloadObj[idField] = contains[0].taskId.ToString();
                }
            }

            void TryResolveAssignees()
            {
                if (payloadObj["assigneeIds"] is not null) return;
                if (payloadObj["assigneeNames"] is not JsonArray namesArr) return;

                var ids = new List<string>();
                foreach (var n in namesArr)
                {
                    var name = Norm(n?.GetValue<string>());
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var match = members.FirstOrDefault(m => Norm(m.DisplayName) == name)
                                ?? members.FirstOrDefault(m => Norm(m.Email) == name);
                    if (match is not null)
                        ids.Add(match.UserId.ToString());
                }

                if (ids.Count > 0)
                    payloadObj["assigneeIds"] = new JsonArray(ids.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            }

            // Best-effort resolution for common fields
            TryResolveColumn("columnName", "columnId");
            TryResolveColumn("targetColumnName", "targetColumnId");
            TryResolveMilestone("milestoneName", "milestoneId");
            TryResolveBacklog("backlogTitle", "backlogItemId");
            TryResolveTask("taskTitle", "taskId");
            TryResolveAssignees();

            // If user already provided an ID as a string elsewhere, normalize it into the expected field.
            if (payloadObj["targetColumnId"] is null)
                SetPayloadGuidIfMissing("targetColumnId", TryParseGuid(GetPayloadString("columnId")));

            // Deterministic Draft enrichment for common follow-up commands.
            // Example: "assign all Front-End member into drawing class diagram tasks".
            var actionType = draftObj["actionType"]?.GetValue<string>()?.Trim();
            if (string.Equals(actionType, "replace_assignees", StringComparison.Ordinal))
            {
                var msg = (userMessage ?? "");
                var msgNorm = Norm(msg);

                static bool MentionsAll(string msgNorm)
                    => msgNorm.Contains("assign all")
                       || msgNorm.Contains("all member")
                       || msgNorm.Contains("all members")
                       || msgNorm.Contains("tất cả")
                       || msgNorm.Contains("tat ca")
                       || msgNorm.Contains("toàn bộ")
                       || msgNorm.Contains("toan bo");

                static bool MentionsFrontend(string msgNorm)
                    => msgNorm.Contains("front-end")
                       || msgNorm.Contains("frontend")
                       || msgNorm.Contains("front end")
                       || msgNorm.Contains("fe ")
                       || msgNorm.EndsWith(" fe")
                       || msgNorm.Contains("fe member")
                       || msgNorm.Contains("front end member")
                       || msgNorm.Contains("frontend member");

                static bool LooksLikeFrontendMember(MemberCandidate m)
                {
                    bool Has(string? v, string token)
                        => !string.IsNullOrWhiteSpace(v) && v.Trim().Contains(token, StringComparison.OrdinalIgnoreCase);

                    if (Has(m.PrimaryRole, "frontend")) return true;
                    if (Has(m.AssignedRole, "frontend") || Has(m.AssignedRole, "front-end") || Has(m.AssignedRole, "front end")) return true;
                    if (Has(m.DesiredPosition, "frontend") || Has(m.DesiredPosition, "front-end") || Has(m.DesiredPosition, "front end")) return true;
                    if (m.SkillTags.Any(t => t.Equals("react", StringComparison.OrdinalIgnoreCase)
                                             || t.Equals("vue", StringComparison.OrdinalIgnoreCase)
                                             || t.Equals("angular", StringComparison.OrdinalIgnoreCase)
                                             || t.Equals("typescript", StringComparison.OrdinalIgnoreCase)
                                             || t.Equals("tailwind", StringComparison.OrdinalIgnoreCase)))
                        return true;

                    return false;
                }

                // Infer task from message by matching candidate task titles (unique match only).
                if (payloadObj["taskId"] is null)
                {
                    var byContains = taskCands
                        .Where(t => !string.IsNullOrWhiteSpace(t.title) && msgNorm.Contains(Norm(t.title)))
                        .ToList();
                    if (byContains.Count == 1)
                    {
                        payloadObj["taskId"] = byContains[0].taskId.ToString();
                        if (payloadObj["taskTitle"] is null)
                            payloadObj["taskTitle"] = byContains[0].title;
                    }
                }

                // If still missing, fall back to last draft title (when this is a follow-up).
                if (payloadObj["taskId"] is null && payloadObj["taskTitle"] is null && last is not null)
                {
                    try
                    {
                        using var lastDoc = JsonDocument.Parse(last.DraftJson);
                        var lastRoot = lastDoc.RootElement;
                        if (lastRoot.ValueKind == JsonValueKind.Object
                            && lastRoot.TryGetProperty("actionPayload", out var lastPayload)
                            && lastPayload.ValueKind == JsonValueKind.Object)
                        {
                            var lastTitle = GetString(lastPayload, "title") ?? GetString(lastPayload, "taskTitle");
                            if (!string.IsNullOrWhiteSpace(lastTitle))
                                payloadObj["taskTitle"] = lastTitle;
                        }
                    }
                    catch
                    {
                        // ignore malformed cache
                    }

                    TryResolveTask("taskTitle", "taskId");
                }

                // Infer "assign all members" when user explicitly says all.
                if (payloadObj["assigneeIds"] is null)
                {
                    var wantsFrontend = MentionsFrontend(msgNorm);
                    var wantsAll = MentionsAll(msgNorm);

                    // If user says "Front-End member" without specifying names, interpret as "assign all Front-End members".
                    // This is safe because Draft is editable and Commit validates.
                    if (wantsFrontend)
                    {
                        var fe = members.Where(LooksLikeFrontendMember).ToList();
                        if (fe.Count > 0)
                        {
                            payloadObj["assigneeIds"] = new JsonArray(fe.Select(x => (JsonNode?)JsonValue.Create(x.UserId.ToString())).ToArray());
                            payloadObj["assigneeNames"] = new JsonArray(fe.Select(x => (JsonNode?)JsonValue.Create(x.DisplayName)).ToArray());
                        }
                    }
                    else if (wantsAll)
                    {
                        var ids = members.Select(m => m.UserId.ToString()).ToList();
                        if (ids.Count > 0)
                        {
                            payloadObj["assigneeIds"] = new JsonArray(ids.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
                            payloadObj["assigneeNames"] = new JsonArray(members.Select(m => (JsonNode?)JsonValue.Create(m.DisplayName)).ToArray());
                        }
                    }
                }

                // If we now have enough for commit, don't ask redundant questions.
                var hasTaskId = payloadObj["taskId"] is not null;
                var hasAssignees = payloadObj["assigneeIds"] is JsonArray a && a.Count > 0;
                if (hasTaskId && hasAssignees)
                {
                    responseNode["questions"] = new JsonArray();
                    var title = payloadObj["taskTitle"]?.GetValue<string>() ?? "(selected task)";
                    responseNode["answerText"] = $"Got it. I prepared an assignee update for task '{title}'. Review/edit the assignees and confirm to commit.";
                }
            }
        }

        // Persist the latest draft for follow-up turns in this chat.
        if (responseNode["draft"] is JsonObject d2)
        {
            cache.Set(cacheKey,
                new AssistantConversationState(d2.ToJsonString(), DateTime.UtcNow),
                new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) });
        }

        return Content(responseNode.ToJsonString(), "application/json");
    }

    private static async Task<IReadOnlyList<MemberCandidate>> BuildMemberCandidatesAsync(AppDbContext db, Guid groupId, CancellationToken ct)
    {
        static (string? PrimaryRole, IReadOnlyList<string> SkillTags) ParseSkills(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return (null, Array.Empty<string>());

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                string? GetStringProp(JsonElement el, string name)
                    => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                        ? p.GetString()
                        : null;

                var primary =
                    GetStringProp(root, "primary_role")
                    ?? GetStringProp(root, "primaryRole")
                    ?? GetStringProp(root, "primary");

                var tags = new List<string>();

                void ReadStringArray(string prop)
                {
                    if (root.ValueKind != JsonValueKind.Object) return;
                    if (!root.TryGetProperty(prop, out var arr)) return;
                    if (arr.ValueKind != JsonValueKind.Array) return;
                    foreach (var it in arr.EnumerateArray())
                    {
                        if (it.ValueKind == JsonValueKind.String)
                        {
                            var s = it.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) tags.Add(s.Trim());
                        }
                    }
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in root.EnumerateArray())
                    {
                        if (it.ValueKind == JsonValueKind.String)
                        {
                            var s = it.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) tags.Add(s.Trim());
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.String)
                {
                    var s = root.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) tags.Add(s.Trim());
                }
                else
                {
                    ReadStringArray("skill_tags");
                    ReadStringArray("skills");
                    ReadStringArray("skillTags");
                    ReadStringArray("stack");
                }

                var cleaned = tags
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList();

                return (string.IsNullOrWhiteSpace(primary) ? null : primary.Trim(), cleaned);
            }
            catch
            {
                return (null, Array.Empty<string>());
            }
        }

        var rows = await (
            from m in db.group_members.AsNoTracking()
            join u in db.users.AsNoTracking() on m.user_id equals u.user_id
            join p in db.position_lists.AsNoTracking() on u.desired_position_id equals p.position_id into pj
            from p in pj.DefaultIfEmpty()
            where m.group_id == groupId && (m.status == "member" || m.status == "leader")
            orderby m.status descending, m.joined_at
            select new
            {
                u.user_id,
                u.display_name,
                u.email,
                memberRole = m.status,
                skills = u.skills,
                desiredPosition = p == null ? null : p.position_name,
                groupMemberId = m.group_member_id
            }
        ).ToListAsync(ct);

        var memberIds = rows.Select(x => x.groupMemberId).ToList();
        var assignedRolesLookup = await db.group_member_roles.AsNoTracking()
            .Where(r => memberIds.Contains(r.group_member_id))
            .GroupBy(r => r.group_member_id)
            .Select(g => new { groupMemberId = g.Key, role = g.OrderByDescending(r => r.assigned_at).Select(r => r.role_name).FirstOrDefault() })
            .ToDictionaryAsync(x => x.groupMemberId, x => x.role, ct);

        return rows.Select(x =>
        {
            var parsed = ParseSkills(x.skills);
            assignedRolesLookup.TryGetValue(x.groupMemberId, out var assignedRole);
            return new MemberCandidate(
                x.user_id,
                x.display_name,
                x.email,
                x.memberRole,
                assignedRole,
                x.desiredPosition,
                parsed.PrimaryRole,
                parsed.SkillTags);
        }).ToList();
    }

    private static bool IsFollowUpEditRequest(string message)
    {
        var t = (message ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return false;

        // If user is explicitly creating a new item, don't treat as follow-up.
        if (t.Contains("create a task") || t.Contains("new task") || t.Contains("add a task")
            || t.Contains("tạo task") || t.Contains("tao task") || t.Contains("tạo công việc") || t.Contains("tao cong viec")
            || t.Contains("create a milestone") || t.Contains("new milestone") || t.Contains("create milestone")
            || t.Contains("tạo milestone") || t.Contains("tao milestone"))
            return false;

        return t.Contains("write")
               || t.Contains("rewrite")
               || t.Contains("expand")
               || t.Contains("more")
               || t.Contains("description")
               || t.Contains("acceptance")
               || t.Contains("criteria")
               || t.Contains("test")
               || t.Contains("due date")
               || t.Contains("deadline")
               || t.Contains("tomorrow")
               || t.Contains("next week")
               || t.Contains("next month")
               || t.Contains("rename")
               || t.Contains("change")
               || t.Contains("update")
               || t.Contains("the name is")
               || t.Contains("name is")
               || t.Contains("milestone name")
               || t.Contains("target date")
               || t.Contains("ngày")
               || t.Contains("ngay")
               || t.Contains("ngày mai")
               || t.Contains("ngay mai")
               || t.Contains("mai")
               || t.Contains("hạn")
               || t.Contains("han")
               || t.Contains("deadline")
               || t.Contains("đổi")
               || t.Contains("doi")
               || t.Contains("đổi tên")
               || t.Contains("doi ten")
               || t.Contains("tên")
               || t.Contains("ten")
               || t.Contains("milestone")
               || t.Contains("chi tiết")
               || t.Contains("chi tiet")
               || t.Contains("bổ sung")
               || t.Contains("bo sung")
               || t.Contains("viết thêm")
               || t.Contains("viet them")
               || t.Contains("mở rộng")
               || t.Contains("mo rong")
               || t.Contains("cập nhật")
               || t.Contains("cap nhat")
               || t.Contains("sửa")
               || t.Contains("sua");
    }

    private static bool IsMilestoneSuitabilityQuestion(string message)
    {
        var t = (message ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return false;

        // English patterns
        if ((t.Contains("milestone") && (t.Contains("suitable") || t.Contains("appropriate") || t.Contains("fit") || t.Contains("match")
            || t.Contains("any") || t.Contains("is there") || t.Contains("check") || t.Contains("which")))
            || t.Contains("suitable milestone")
            || t.Contains("any milestone"))
            return true;

        // Vietnamese patterns
        if ((t.Contains("milestone") || t.Contains("mốc") || t.Contains("moc"))
            && (t.Contains("phù hợp") || t.Contains("phu hop") || t.Contains("có") || t.Contains("co") || t.Contains("kiểm tra") || t.Contains("kiem tra")
                || t.Contains("còn") || t.Contains("con") || t.Contains("nào") || t.Contains("nao") || t.Contains("không") || t.Contains("khong")))
            return true;

        return false;
    }

    [HttpPost("commit")]
    [Authorize]
    public async Task<IActionResult> Commit(Guid groupId, [FromBody] JsonElement body, CancellationToken ct)
    {
        var userId = GetUserId();

        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "request body must be a JSON object" });

        // Accepted request shapes (FE should send the 1st one):
        // 1) { actionType, actionPayload }                       (NEW)
        // 2) { approvedDraft: { actionType, actionPayload } }     (legacy)
        // 3) { draft: { actionType, actionPayload }, ... }        (Phase-A full response)
        var root = body;
        if (root.TryGetProperty("approvedDraft", out var approved)
            && approved.ValueKind == JsonValueKind.Object)
        {
            root = approved;
        }

        var draft = root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;

        // Action-based schema (new)
        var actionType = GetString(draft, "actionType")?.Trim();
        if (!string.IsNullOrWhiteSpace(actionType))
        {
            if (!draft.TryGetProperty("actionPayload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                return BadRequest(new { error = "draft.actionPayload must be an object" });

            switch (actionType)
            {
                case "create_backlog_and_task":
                {
                    var title = GetString(payload, "title");
                    if (string.IsNullOrWhiteSpace(title))
                        return BadRequest(new { error = "actionPayload.title is required" });

                    var description = GetString(payload, "description") ?? "";
                    var priority = GetString(payload, "priority");
                    var dueDate = GetDateTime(payload, "dueDate");
                    var milestoneId = GetGuid(payload, "milestoneId");
                    var milestoneName = GetString(payload, "milestoneName");
                    var milestoneDescription = GetString(payload, "milestoneDescription");
                    var milestoneTargetDate = GetDateOnly(payload, "milestoneTargetDate");
                    var columnId = GetGuid(payload, "columnId");
                    var assigneeIds = GetGuidList(payload, "assigneeIds");

                    var backlogId = await tracking.CreateBacklogItemAsync(groupId, userId,
                        new CreateBacklogItemRequest(
                            Title: title,
                            Description: string.IsNullOrWhiteSpace(description) ? null : description,
                            Priority: string.IsNullOrWhiteSpace(priority) ? null : priority,
                            Category: null,
                            StoryPoints: null,
                            DueDate: dueDate,
                            OwnerUserId: null),
                        ct);

                    // If user asked for a new milestone, Draft may carry milestoneName (with milestoneId null).
                    // Create it deterministically here so Commit remains a single safe operation.
                    if (!milestoneId.HasValue && !string.IsNullOrWhiteSpace(milestoneName))
                    {
                        milestoneId = await tracking.CreateMilestoneAsync(groupId, userId,
                            new CreateMilestoneRequest(
                                Name: milestoneName!,
                                Description: string.IsNullOrWhiteSpace(milestoneDescription) ? null : milestoneDescription,
                                TargetDate: milestoneTargetDate),
                            ct);
                    }

                    if (milestoneId.HasValue)
                    {
                        await tracking.AssignMilestoneItemsAsync(groupId, milestoneId.Value, userId,
                            new AssignMilestoneItemsRequest(new[] { backlogId }), ct);
                    }

                    var promoteColumnId = columnId ?? await PickDefaultColumnIdAsync(groupId, userId, ct);
                    var taskId = await tracking.PromoteBacklogItemAsync(groupId, backlogId, userId,
                        new PromoteBacklogItemRequest(promoteColumnId, TaskStatus: null, TaskDueDate: null), ct);

                    if (assigneeIds is not null && assigneeIds.Count > 0)
                        await board.ReplaceAssigneesAsync(groupId, taskId, userId, new ReplaceAssigneesRequest(assigneeIds), ct);

                    return Ok(new { ok = true, created = true, backlogItemId = backlogId, taskId });
                }

                case "create_task":
                {
                    var title = GetString(payload, "title");
                    if (string.IsNullOrWhiteSpace(title))
                        return BadRequest(new { error = "actionPayload.title is required" });

                    var effectiveColumnId = GetGuid(payload, "columnId") ?? await PickDefaultColumnIdAsync(groupId, userId, ct);
                    var createReq = new CreateTaskRequest(
                        ColumnId: effectiveColumnId,
                        Title: title,
                        Description: GetString(payload, "description"),
                        Priority: GetString(payload, "priority"),
                        Status: GetString(payload, "status"),
                        DueDate: GetDateTime(payload, "dueDate"),
                        BacklogItemId: GetGuid(payload, "backlogItemId"),
                        AssigneeIds: GetGuidList(payload, "assigneeIds"));

                    var taskId = await board.CreateTaskAsync(groupId, userId, createReq, ct);
                    return Ok(new { ok = true, created = true, taskId });
                }

                case "update_task":
                {
                    var taskId = GetGuid(payload, "taskId");
                    if (!taskId.HasValue)
                        return BadRequest(new { error = "actionPayload.taskId is required" });

                    var title = GetString(payload, "title");
                    if (string.IsNullOrWhiteSpace(title))
                        return BadRequest(new { error = "actionPayload.title is required for update_task" });

                    var updateReq = new UpdateTaskRequest(
                        ColumnId: GetGuid(payload, "columnId"),
                        Title: title,
                        Description: GetString(payload, "description"),
                        Priority: GetString(payload, "priority"),
                        Status: GetString(payload, "status"),
                        DueDate: GetDateTime(payload, "dueDate"),
                        BacklogItemId: GetGuid(payload, "backlogItemId"),
                        AssigneeIds: GetGuidList(payload, "assigneeIds"));

                    await board.UpdateTaskAsync(groupId, taskId.Value, userId, updateReq, ct);
                    return Ok(new { ok = true, updated = true, taskId });
                }

                case "delete_task":
                {
                    var taskId = GetGuid(payload, "taskId");
                    if (!taskId.HasValue)
                        return BadRequest(new { error = "actionPayload.taskId is required" });

                    await board.DeleteTaskAsync(groupId, taskId.Value, userId, ct);
                    return Ok(new { ok = true, deleted = true, taskId });
                }

                case "move_task":
                {
                    var taskId = GetGuid(payload, "taskId");
                    var columnId = GetGuid(payload, "targetColumnId") ?? GetGuid(payload, "columnId");
                    if (!taskId.HasValue || !columnId.HasValue)
                        return BadRequest(new { error = "actionPayload.taskId and actionPayload.targetColumnId are required" });

                    var moveReq = new MoveTaskRequest(
                        ColumnId: columnId.Value,
                        PrevTaskId: GetGuid(payload, "prevTaskId"),
                        NextTaskId: GetGuid(payload, "nextTaskId"));

                    var res = await board.MoveTaskAsync(groupId, taskId.Value, userId, moveReq, ct);
                    return Ok(new { ok = true, moved = true, res.TaskId, res.ColumnId, res.SortOrder });
                }

                case "replace_assignees":
                {
                    var taskId = GetGuid(payload, "taskId");
                    var assigneeIds = GetGuidList(payload, "assigneeIds");
                    if (!taskId.HasValue || assigneeIds is null || assigneeIds.Count == 0)
                        return BadRequest(new { error = "actionPayload.taskId and non-empty actionPayload.assigneeIds are required" });

                    await board.ReplaceAssigneesAsync(groupId, taskId.Value, userId, new ReplaceAssigneesRequest(assigneeIds), ct);
                    return Ok(new { ok = true, updated = true, taskId, assigneeIds });
                }

                case "add_comment":
                {
                    var taskId = GetGuid(payload, "taskId");
                    var content = GetString(payload, "content");
                    if (!taskId.HasValue || string.IsNullOrWhiteSpace(content))
                        return BadRequest(new { error = "actionPayload.taskId and actionPayload.content are required" });

                    var commentId = await board.AddCommentAsync(groupId, taskId.Value, userId, new CreateCommentRequest(content), ct);
                    return Ok(new { ok = true, created = true, commentId, taskId });
                }

                case "delete_comment":
                {
                    var commentId = GetGuid(payload, "commentId");
                    if (!commentId.HasValue)
                        return BadRequest(new { error = "actionPayload.commentId is required" });

                    await board.DeleteCommentAsync(groupId, commentId.Value, userId, ct);
                    return Ok(new { ok = true, deleted = true, commentId });
                }

                case "create_backlog_item":
                {
                    var title = GetString(payload, "title");
                    if (string.IsNullOrWhiteSpace(title))
                        return BadRequest(new { error = "actionPayload.title is required" });

                    var backlogItemId = await tracking.CreateBacklogItemAsync(groupId, userId,
                        new CreateBacklogItemRequest(
                            Title: title,
                            Description: GetString(payload, "description"),
                            Priority: GetString(payload, "priority"),
                            Category: GetString(payload, "category"),
                            StoryPoints: GetInt(payload, "storyPoints"),
                            DueDate: GetDateTime(payload, "dueDate"),
                            OwnerUserId: GetGuid(payload, "ownerUserId")),
                        ct);

                    return Ok(new { ok = true, created = true, backlogItemId });
                }

                case "update_backlog_item":
                {
                    var backlogItemId = GetGuid(payload, "backlogItemId");
                    if (!backlogItemId.HasValue)
                        return BadRequest(new { error = "actionPayload.backlogItemId is required" });

                    var title = GetString(payload, "title");
                    var status = GetString(payload, "status");
                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(status))
                        return BadRequest(new { error = "actionPayload.title and actionPayload.status are required for update_backlog_item" });

                    await tracking.UpdateBacklogItemAsync(groupId, backlogItemId.Value, userId,
                        new UpdateBacklogItemRequest(
                            Title: title,
                            Description: GetString(payload, "description"),
                            Priority: GetString(payload, "priority"),
                            Category: GetString(payload, "category"),
                            StoryPoints: GetInt(payload, "storyPoints"),
                            DueDate: GetDateTime(payload, "dueDate"),
                            Status: status,
                            OwnerUserId: GetGuid(payload, "ownerUserId")),
                        ct);

                    return Ok(new { ok = true, updated = true, backlogItemId });
                }

                case "archive_backlog_item":
                {
                    var backlogItemId = GetGuid(payload, "backlogItemId");
                    if (!backlogItemId.HasValue)
                        return BadRequest(new { error = "actionPayload.backlogItemId is required" });

                    await tracking.ArchiveBacklogItemAsync(groupId, backlogItemId.Value, userId, ct);
                    return Ok(new { ok = true, archived = true, backlogItemId });
                }

                case "promote_backlog_item":
                {
                    var backlogItemId = GetGuid(payload, "backlogItemId");
                    var columnId = GetGuid(payload, "columnId") ?? await PickDefaultColumnIdAsync(groupId, userId, ct);
                    if (!backlogItemId.HasValue)
                        return BadRequest(new { error = "actionPayload.backlogItemId is required" });

                    var taskId = await tracking.PromoteBacklogItemAsync(groupId, backlogItemId.Value, userId,
                        new PromoteBacklogItemRequest(
                            ColumnId: columnId,
                            TaskStatus: GetString(payload, "taskStatus"),
                            TaskDueDate: GetDateTime(payload, "taskDueDate")),
                        ct);

                    return Ok(new { ok = true, promoted = true, backlogItemId, taskId });
                }

                case "create_milestone":
                {
                    var name = GetString(payload, "name");
                    if (string.IsNullOrWhiteSpace(name))
                        return BadRequest(new { error = "actionPayload.name is required" });

                    var milestoneId = await tracking.CreateMilestoneAsync(groupId, userId,
                        new CreateMilestoneRequest(
                            Name: name,
                            Description: GetString(payload, "description"),
                            TargetDate: GetDateOnly(payload, "targetDate")),
                        ct);

                    return Ok(new { ok = true, created = true, milestoneId });
                }

                case "update_milestone":
                {
                    var milestoneId = GetGuid(payload, "milestoneId");
                    if (!milestoneId.HasValue)
                        return BadRequest(new { error = "actionPayload.milestoneId is required" });

                    var name = GetString(payload, "name");
                    var status = GetString(payload, "status");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(status))
                        return BadRequest(new { error = "actionPayload.name and actionPayload.status are required for update_milestone" });

                    await tracking.UpdateMilestoneAsync(groupId, milestoneId.Value, userId,
                        new UpdateMilestoneRequest(
                            Name: name,
                            Description: GetString(payload, "description"),
                            TargetDate: GetDateOnly(payload, "targetDate"),
                            Status: status,
                            CompletedAt: GetDateTime(payload, "completedAt")),
                        ct);

                    return Ok(new { ok = true, updated = true, milestoneId });
                }

                case "delete_milestone":
                {
                    var milestoneId = GetGuid(payload, "milestoneId");
                    if (!milestoneId.HasValue)
                        return BadRequest(new { error = "actionPayload.milestoneId is required" });

                    await tracking.DeleteMilestoneAsync(groupId, milestoneId.Value, userId, ct);
                    return Ok(new { ok = true, deleted = true, milestoneId });
                }

                case "assign_milestone_items":
                {
                    var milestoneId = GetGuid(payload, "milestoneId");
                    var backlogIds = GetGuidList(payload, "backlogItemIds");
                    if (!milestoneId.HasValue || backlogIds is null || backlogIds.Count == 0)
                        return BadRequest(new { error = "actionPayload.milestoneId and non-empty actionPayload.backlogItemIds are required" });

                    await tracking.AssignMilestoneItemsAsync(groupId, milestoneId.Value, userId,
                        new AssignMilestoneItemsRequest(backlogIds), ct);

                    return Ok(new { ok = true, updated = true, milestoneId, backlogItemIds = backlogIds });
                }

                case "remove_milestone_item":
                {
                    var milestoneId = GetGuid(payload, "milestoneId");
                    var backlogItemId = GetGuid(payload, "backlogItemId");
                    if (!milestoneId.HasValue || !backlogItemId.HasValue)
                        return BadRequest(new { error = "actionPayload.milestoneId and actionPayload.backlogItemId are required" });

                    await tracking.RemoveMilestoneItemAsync(groupId, milestoneId.Value, backlogItemId.Value, userId, ct);
                    return Ok(new { ok = true, removed = true, milestoneId, backlogItemId });
                }

                case "extend_milestone":
                {
                    var milestoneId = GetGuid(payload, "milestoneId");
                    var newTargetDate = GetDateOnly(payload, "newTargetDate");
                    if (!milestoneId.HasValue || !newTargetDate.HasValue)
                        return BadRequest(new { error = "actionPayload.milestoneId and actionPayload.newTargetDate are required" });

                    var res = await tracking.ExtendMilestoneAsync(groupId, milestoneId.Value, userId,
                        new ExtendMilestoneRequest(newTargetDate.Value), ct);

                    return Ok(new { ok = true, action = res.Action, res.Message, milestoneId });
                }

                case "move_milestone_tasks":
                {
                    var milestoneId = GetGuid(payload, "milestoneId");
                    if (!milestoneId.HasValue)
                        return BadRequest(new { error = "actionPayload.milestoneId is required" });

                    var createNew = GetBool(payload, "createNewMilestone") ?? false;
                    var req2 = new MoveMilestoneTasksRequest(
                        TargetMilestoneId: GetGuid(payload, "targetMilestoneId"),
                        CreateNewMilestone: createNew,
                        NewMilestoneName: GetString(payload, "newMilestoneName"),
                        NewMilestoneTargetDate: GetDateOnly(payload, "newMilestoneTargetDate"),
                        NewMilestoneDescription: GetString(payload, "newMilestoneDescription"));

                    var res = await tracking.MoveMilestoneTasksAsync(groupId, milestoneId.Value, userId, req2, ct);
                    return Ok(new { ok = true, res.Action, res.Message, res.NewMilestoneId });
                }

                default:
                    return BadRequest(new { error = "Unsupported actionType", actionType });
            }
        }

        // Legacy schema (previous create/update behavior)
        {
            var decision = GetString(draft, "dedupeDecision")?.ToLowerInvariant();
            if (decision == "ignore")
                return Ok(new { ok = true, skipped = true, reason = "user_choice_ignore" });

            var mode = GetString(draft, "mode")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(mode) || mode == "auto")
                mode = "backlog-first";

            var title = GetString(draft, "title");
            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { error = "draft.title is required" });

            var description = GetString(draft, "description") ?? "";
            var priority = GetString(draft, "priority");

            var milestoneId = GetGuid(draft, "milestoneId");
            var columnId = GetGuid(draft, "columnId");
            var assigneeIds = GetGuidList(draft, "assigneeIds");

            var dedupeTargetTaskId = GetGuid(draft, "dedupeTarget", "taskId");
            var dedupeTargetBacklogId = GetGuid(draft, "dedupeTarget", "backlogItemId");

            if (decision == "update_existing")
            {
                if (dedupeTargetTaskId.HasValue)
                {
                    var updateReq = new UpdateTaskRequest(
                        ColumnId: columnId,
                        Title: title,
                        Description: string.IsNullOrWhiteSpace(description) ? null : description,
                        Priority: string.IsNullOrWhiteSpace(priority) ? null : priority,
                        Status: GetString(draft, "status"),
                        DueDate: GetDateTime(draft, "dueDate"),
                        BacklogItemId: null,
                        AssigneeIds: assigneeIds);

                    await board.UpdateTaskAsync(groupId, dedupeTargetTaskId.Value, userId, updateReq, ct);
                    return Ok(new { ok = true, updated = true, taskId = dedupeTargetTaskId });
                }

                if (dedupeTargetBacklogId.HasValue)
                {
                    var status = GetString(draft, "backlogStatus") ?? "planned";
                    var updateReq = new UpdateBacklogItemRequest(
                        Title: title,
                        Description: string.IsNullOrWhiteSpace(description) ? null : description,
                        Priority: string.IsNullOrWhiteSpace(priority) ? null : priority,
                        Category: null,
                        StoryPoints: null,
                        DueDate: GetDateTime(draft, "dueDate"),
                        Status: status,
                        OwnerUserId: null);

                    await tracking.UpdateBacklogItemAsync(groupId, dedupeTargetBacklogId.Value, userId, updateReq, ct);
                    return Ok(new { ok = true, updated = true, backlogItemId = dedupeTargetBacklogId });
                }

                return BadRequest(new { error = "update_existing requires dedupeTarget.taskId or dedupeTarget.backlogItemId" });
            }

            // Create new
            if (mode == "task-first")
            {
                var effectiveColumnId = columnId ?? await PickDefaultColumnIdAsync(groupId, userId, ct);
                var createTaskReq = new CreateTaskRequest(
                    ColumnId: effectiveColumnId,
                    Title: title,
                    Description: string.IsNullOrWhiteSpace(description) ? null : description,
                    Priority: string.IsNullOrWhiteSpace(priority) ? null : priority,
                    Status: GetString(draft, "status"),
                    DueDate: GetDateTime(draft, "dueDate"),
                    BacklogItemId: null,
                    AssigneeIds: assigneeIds);

                var taskId = await board.CreateTaskAsync(groupId, userId, createTaskReq, ct);
                return Ok(new { ok = true, created = true, taskId });
            }

            // Default: backlog-first then promote (if possible)
            var backlogId = await tracking.CreateBacklogItemAsync(groupId, userId,
                new CreateBacklogItemRequest(
                    Title: title,
                    Description: string.IsNullOrWhiteSpace(description) ? null : description,
                    Priority: string.IsNullOrWhiteSpace(priority) ? null : priority,
                    Category: null,
                    StoryPoints: null,
                    DueDate: GetDateTime(draft, "dueDate"),
                    OwnerUserId: null),
                ct);

            if (milestoneId.HasValue)
            {
                await tracking.AssignMilestoneItemsAsync(groupId, milestoneId.Value, userId,
                    new AssignMilestoneItemsRequest(new[] { backlogId }), ct);
            }

            var promoteColumnId = columnId ?? await PickDefaultColumnIdAsync(groupId, userId, ct);
            var taskIdCreated = await tracking.PromoteBacklogItemAsync(groupId, backlogId, userId,
                new PromoteBacklogItemRequest(promoteColumnId, TaskStatus: null, TaskDueDate: null), ct);

            if (assigneeIds is not null && assigneeIds.Count > 0)
            {
                await board.ReplaceAssigneesAsync(groupId, taskIdCreated, userId, new ReplaceAssigneesRequest(assigneeIds), ct);
            }

            return Ok(new { ok = true, created = true, backlogItemId = backlogId, taskId = taskIdCreated });
        }
    }

    private static JsonDocument MergeDedupeIntoDraft(JsonElement upstream, IReadOnlyList<object> dedupeSimilar)
    {
        // Response to FE: include upstream keys, plus `dedupe`.
        // If upstream already has `dedupe`, keep it and add `similarItems`.
        var baseObj = JsonSerializer.Deserialize<Dictionary<string, object?>>(upstream.GetRawText())
                     ?? new Dictionary<string, object?>();

        if (!baseObj.TryGetValue("dedupe", out var dedupeObj) || dedupeObj is null)
            dedupeObj = new Dictionary<string, object?>();

        var dedupeDict = dedupeObj as Dictionary<string, object?>
                         ?? JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(dedupeObj))
                         ?? new Dictionary<string, object?>();

        dedupeDict["similarItems"] = dedupeSimilar;
        if (dedupeSimilar.Count > 0)
            dedupeDict["note"] = "Found similar recent tasks. Please confirm: create new / update existing / ignore.";

        baseObj["dedupe"] = dedupeDict;
        return JsonDocument.Parse(JsonSerializer.Serialize(baseObj));
    }

    private async Task<IReadOnlyList<object>> ComputeSimilarTasksAsync(string userText, IReadOnlyList<RecentTaskRow> recentTasks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userText) || recentTasks.Count == 0)
            return Array.Empty<object>();

        var candidates = recentTasks.Select(t =>
        {
            var payload = string.IsNullOrWhiteSpace(t.Description) ? t.Title : (t.Title + "\n" + t.Description);
            return new AiLlmCandidate(
                Key: t.TaskId.ToString(),
                Id: t.TaskId,
                Title: t.Title,
                Description: t.Description,
                Payload: payload,
                Metadata: null);
        }).ToList();

        var res = await llm.RerankAsync(new AiLlmRerankRequest(
            QueryType: "topic",
            QueryText: userText,
            Candidates: candidates,
            Context: new Dictionary<string, string>
            {
                ["mode"] = "topic",
                ["topN"] = "5",
                ["withReasons"] = "false"
            }), ct);

        var ranked = res.Items ?? Array.Empty<AiLlmRerankedItem>();
        var top = ranked
            .Where(x => x.Key is not null)
            .OrderByDescending(x => x.FinalScore)
            .Take(3)
            .ToList();

        if (top.Count == 0)
            return Array.Empty<object>();

        var thresholdHit = top.Any(x => x.FinalScore >= 85);
        if (!thresholdHit)
            return Array.Empty<object>();

        // Build minimal similar items payload for UI
        var dictById = recentTasks.ToDictionary(t => t.TaskId.ToString(), t => t);

        return top.Select(x =>
        {
            dictById.TryGetValue(x.Key!, out var t);
            return new
            {
                kind = "task",
                id = x.Key,
                title = t is null ? null : t.Title,
                status = t is null ? null : t.Status,
                updatedAt = t is null ? (DateTime?)null : t.UpdatedAt,
                similarityScore = Math.Round(x.FinalScore, 2),
                verdict = "needs_check"
            };
        }).Cast<object>().ToList();
    }

    private async Task<Guid> PickDefaultColumnIdAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var b = await board.GetBoardAsync(groupId, userId, status: null, page: null, pageSize: null, ct);
        var col = b.Columns.FirstOrDefault(c => !c.IsDone) ?? b.Columns.FirstOrDefault();
        if (col is null) throw new InvalidOperationException("Board has no columns");
        return col.ColumnId;
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetString(JsonElement root, string parent, string child)
        => root.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object ? GetString(p, child) : null;

    private static Guid? GetGuid(JsonElement root, string name)
    {
        var s = GetString(root, name);
        return Guid.TryParse(s, out var id) ? id : null;
    }

    private static Guid? GetGuid(JsonElement root, string parent, string child)
    {
        var s = GetString(root, parent, child);
        return Guid.TryParse(s, out var id) ? id : null;
    }

    private static DateTime? GetDateTime(JsonElement root, string name)
    {
        var s = GetString(root, name);
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static DateOnly? GetDateOnly(JsonElement root, string name)
    {
        var s = GetString(root, name);
        return DateOnly.TryParse(s, out var d) ? d : null;
    }

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n2)) return n2;
        return null;
    }

    private static bool? GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
        return null;
    }

    private static IReadOnlyList<Guid>? GetGuidList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<Guid>();
        foreach (var x in arr.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.String) continue;
            if (Guid.TryParse(x.GetString(), out var id))
                list.Add(id);
        }

        return list.Count == 0 ? null : list;
    }
}