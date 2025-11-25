using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Ai.Models;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class AiMatchingQueries : IAiMatchingQueries
{
    private readonly AppDbContext _db;

    public AiMatchingQueries(AppDbContext db)
    {
        _db = db;
    }

    public async Task<StudentProfileSnapshot?> GetStudentProfileAsync(Guid userId, Guid semesterId, CancellationToken ct)
    {
        var row = await _db.mv_students_pools
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.user_id == userId && x.semester_id == semesterId, ct);

        return row is null ? null : MapStudent(row);
    }

    public async Task<IReadOnlyList<StudentProfileSnapshot>> ListUnassignedStudentsAsync(Guid semesterId, Guid? majorId, CancellationToken ct)
    {
        var query = _db.mv_students_pools
            .AsNoTracking()
            .Where(x => x.semester_id == semesterId);

        if (majorId.HasValue)
            query = query.Where(x => x.major_id == majorId);

        var rows = await query.ToListAsync(ct);
        return rows
            .Where(r => r.user_id.HasValue && r.major_id.HasValue && r.semester_id.HasValue)
            .Select(MapStudent)
            .ToList();
    }

    public async Task<IReadOnlyList<GroupCapacitySnapshot>> ListGroupCapacitiesAsync(Guid semesterId, Guid? majorId, CancellationToken ct)
    {
        var query = _db.mv_group_capacities
            .AsNoTracking()
            .Where(x => x.semester_id == semesterId)
            .Where(x => x.remaining_slots > 0);

        if (majorId.HasValue)
            query = query.Where(x => x.major_id == majorId);

        var rows = await query.ToListAsync(ct);
        return rows
            .Where(r => r.group_id.HasValue && r.max_members.HasValue && r.current_members.HasValue && r.remaining_slots.HasValue)
            .Select(r => new GroupCapacitySnapshot(
                r.group_id!.Value,
                r.semester_id ?? semesterId,
                r.major_id,
                r.name ?? string.Empty,
                r.description,
                (int)r.max_members!.Value,
                (int)r.current_members!.Value,
                (int)r.remaining_slots!.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<TopicMatchSnapshot>> ListTopicMatchesAsync(Guid groupId, CancellationToken ct)
    {
        var rows = await _db.mv_group_topic_matches
            .AsNoTracking()
            .Where(x => x.group_id == groupId)
            .ToListAsync(ct);

        return rows
            .Where(r => r.topic_id.HasValue && r.group_id.HasValue && r.semester_id.HasValue)
            .Select(r => new TopicMatchSnapshot(
                r.group_id!.Value,
                r.topic_id!.Value,
                r.semester_id!.Value,
                r.major_id,
                r.title ?? string.Empty,
                r.topic_desc,
                r.simple_score ?? 0))
            .ToList();
    }

    public async Task<IReadOnlyList<TopicAvailabilitySnapshot>> ListTopicAvailabilityAsync(Guid semesterId, Guid? majorId, CancellationToken ct)
    {
        var query = _db.vw_topics_availables
            .AsNoTracking()
            .Where(x => x.semester_id == semesterId);

        if (majorId.HasValue)
            query = query.Where(x => x.major_id == majorId);

        var rows = await query.ToListAsync(ct);
        return rows
            .Where(r => r.topic_id.HasValue && r.semester_id.HasValue)
            .Select(r => new TopicAvailabilitySnapshot(
                r.topic_id!.Value,
                r.semester_id!.Value,
                r.major_id,
                r.title ?? string.Empty,
                r.description,
                r.used_by_groups ?? 0,
                r.can_take_more ?? false))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, GroupRoleMixSnapshot>> GetGroupRoleMixAsync(IEnumerable<Guid> groupIds, CancellationToken ct)
    {
        var ids = groupIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, GroupRoleMixSnapshot>();

        var rows = await (
            from gm in _db.group_members
            join u in _db.users on gm.user_id equals u.user_id
            join sp in _db.mv_students_pools on gm.user_id equals sp.user_id into spj
            from sp in spj.Where(x => x.semester_id == gm.semester_id).DefaultIfEmpty()
            where ids.Contains(gm.group_id)
                  && (gm.status == "leader" || gm.status == "member")
            select new
            {
                gm.group_id,
                Role = sp.primary_role,
                u.skills
            }).ToListAsync(ct);

        var result = ids.ToDictionary(id => id, id => new GroupRoleMixSnapshot(id, 0, 0, 0));

        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.group_id, out var current))
                continue;

            var normalized = AiRoleHelper.Parse(row.Role ?? ExtractRoleFromJson(row.skills));
            result[row.group_id] = normalized switch
            {
                AiPrimaryRole.Frontend => current with { FrontendCount = current.FrontendCount + 1 },
                AiPrimaryRole.Backend => current with { BackendCount = current.BackendCount + 1 },
                AiPrimaryRole.Other => current with { OtherCount = current.OtherCount + 1 },
                _ => current
            };
        }

        return result;
    }

    public Task RefreshStudentsPoolAsync(CancellationToken ct)
        => _db.Database.ExecuteSqlRawAsync("REFRESH MATERIALIZED VIEW teammy.mv_students_pool", ct);

    private static StudentProfileSnapshot MapStudent(mv_students_pool row)
    {
        if (!row.user_id.HasValue || !row.major_id.HasValue || !row.semester_id.HasValue)
            throw new InvalidOperationException("Student view row missing identifiers");

        return new StudentProfileSnapshot(
            row.user_id.Value,
            row.major_id.Value,
            row.semester_id.Value,
            row.display_name ?? string.Empty,
            row.primary_role,
            row.skills,
            row.skills_completed ?? false
        );
    }

    private static string? ExtractRoleFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("primaryRole", out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
                if (doc.RootElement.TryGetProperty("primary_role", out var snake) && snake.ValueKind == JsonValueKind.String)
                    return snake.GetString();
            }
        }
        catch
        {
            // ignore malformed skill json
        }
        return null;
    }
}
