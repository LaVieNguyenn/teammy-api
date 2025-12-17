using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Topics.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories
{
    public sealed class TopicReadOnlyQueries : ITopicReadOnlyQueries
    {
        private readonly AppDbContext _db;

        public TopicReadOnlyQueries(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<TopicListItemDto>> GetAllAsync(
            string? q,
            Guid? semesterId,
            string? status,
            Guid? majorId,
            CancellationToken ct)
        {
            var src = _db.topics
                .Include(t => t.semester)
                .Include(t => t.major)
                .Include(t => t.created_byNavigation)
                .Include(t => t.mentors)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = $"%{q}%";
                src = src.Where(t =>
                    EF.Functions.ILike(t.title, pattern) ||
                    EF.Functions.ILike(t.description ?? string.Empty, pattern));
            }

            if (semesterId is not null)
                src = src.Where(t => t.semester_id == semesterId);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var st = status.Trim().ToLowerInvariant();
                src = src.Where(t => t.status == st);
            }

            if (majorId is not null)
                src = src.Where(t => t.major_id == majorId);

            var rows = await src
                .OrderByDescending(t => t.created_at)
                .Select(t => new
                {
                    t.topic_id,
                    t.semester_id,
                    SemesterSeason = t.semester.season,
                    SemesterYear = t.semester.year,
                    t.major_id,
                    MajorName = t.major != null ? t.major.major_name : null,
                    t.title,
                    t.description,
                    t.source,
                    t.source_file_name,
                    t.source_file_type,
                    t.source_file_size,
                    t.status,
                    t.created_by,
                    CreatedByName = t.created_byNavigation.display_name,
                    CreatedByEmail = t.created_byNavigation.email,
                    Mentors = t.mentors
                        .OrderBy(m => m.display_name)
                        .Select(m => new TopicMentorDto(
                            m.user_id,
                            m.display_name,
                            m.email))
                        .ToList(),
                    t.created_at,
                    t.skills
                })
                .ToListAsync(ct);

            var topicIds = rows.Select(r => r.topic_id).Distinct().ToList();
            var acceptedGroupMap = await _db.groups.AsNoTracking()
                .Where(g => g.topic_id.HasValue && topicIds.Contains(g.topic_id.Value))
                .Select(g => new
                {
                    g.topic_id,
                    g.group_id,
                    g.name
                })
                .ToListAsync(ct);
            var pendingInviteMap = await (
                    from inv in _db.invitations.AsNoTracking()
                    join g in _db.groups.AsNoTracking() on inv.group_id equals g.group_id
                    where inv.topic_id.HasValue
                          && topicIds.Contains(inv.topic_id.Value)
                          && inv.status == "pending"
                    select new
                    {
                        inv.topic_id,
                        g.group_id,
                        g.name,
                        Status = "Pending mentor invitation!!!"
                    })
                .ToListAsync(ct);

            Dictionary<Guid, List<TopicGroupUsageDto>> BuildGroupDict(IEnumerable<dynamic> source)
                => source
                    .GroupBy(x => (Guid)x.topic_id!)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => new TopicGroupUsageDto((Guid)x.group_id, (string)x.name, (string)x.Status)).ToList());

            IReadOnlyDictionary<Guid, List<TopicGroupUsageDto>> acceptedByTopic =
                BuildGroupDict(acceptedGroupMap.Select(x => new { topic_id = x.topic_id, group_id = x.group_id, name = x.name, Status = "accepted" }));
            IReadOnlyDictionary<Guid, List<TopicGroupUsageDto>> pendingByTopic =
                BuildGroupDict(pendingInviteMap.Select(x => new { topic_id = x.topic_id, group_id = x.group_id, name = x.name, Status = x.Status }));
            IReadOnlyDictionary<Guid, List<TopicGroupUsageDto>> rejectedByTopic =
                BuildGroupDict(await (
                        from inv in _db.invitations.AsNoTracking()
                        join g in _db.groups.AsNoTracking() on inv.group_id equals g.group_id
                        where inv.topic_id.HasValue
                              && topicIds.Contains(inv.topic_id.Value)
                              && inv.status == "rejected"
                        select new
                        {
                            inv.topic_id,
                            g.group_id,
                            g.name,
                            Status = "rejected"
                        })
                    .ToListAsync(ct));

            return rows
                .Select(r => new TopicListItemDto(
                    r.topic_id,
                    r.semester_id,
                    r.SemesterSeason,
                    r.SemesterYear,
                    r.major_id,
                    r.MajorName,
                    r.title,
                    r.description,
                    r.source,
                    BuildFileDto(r.source, r.source_file_name, r.source_file_type, r.source_file_size),
                    r.status,
                    r.created_by,
                    r.CreatedByName,
                    r.CreatedByEmail,
                    r.Mentors,
                    CombineGroups(r.topic_id, acceptedByTopic, pendingByTopic, rejectedByTopic),
                    ParseSkills(r.skills),
                    r.created_at))
                .ToList();
        }

        public async Task<TopicDetailDto?> GetByIdAsync(Guid topicId, CancellationToken ct)
        {
            var row = await _db.topics
                .Include(t => t.semester)
                .Include(t => t.major)
                .Include(t => t.created_byNavigation)
                .Include(t => t.mentors)
                .AsNoTracking()
                .Where(t => t.topic_id == topicId)
                .Select(t => new
                {
                    t.topic_id,
                    t.semester_id,
                    SemesterSeason = t.semester.season,
                    SemesterYear = t.semester.year,
                    t.major_id,
                    MajorName = t.major != null ? t.major.major_name : null,
                    t.title,
                    t.description,
                    t.source,
                    t.source_file_name,
                    t.source_file_type,
                    t.source_file_size,
                    t.status,
                    t.created_by,
                    CreatedByName = t.created_byNavigation.display_name,
                    CreatedByEmail = t.created_byNavigation.email,
                    Mentors = t.mentors
                        .OrderBy(m => m.display_name)
                        .Select(m => new TopicMentorDto(
                            m.user_id,
                            m.display_name,
                            m.email))
                        .ToList(),
                    t.created_at,
                    t.skills
                })
                .FirstOrDefaultAsync(ct);

            if (row is null)
                return null;

            var acceptedGroups = await _db.groups.AsNoTracking()
                .Where(g => g.topic_id == topicId)
                .Select(g => new TopicGroupUsageDto(g.group_id, g.name, "accepted"))
                .ToListAsync(ct);
            var pendingGroups = await (
                    from inv in _db.invitations.AsNoTracking()
                    join g in _db.groups.AsNoTracking() on inv.group_id equals g.group_id
                    where inv.topic_id == topicId && inv.status == "pending"
                    select new TopicGroupUsageDto(g.group_id, g.name, "pending_invitation"))
                .ToListAsync(ct);
            var rejectedGroups = await (
                    from inv in _db.invitations.AsNoTracking()
                    join g in _db.groups.AsNoTracking() on inv.group_id equals g.group_id
                    where inv.topic_id == topicId && inv.status == "rejected"
                    select new TopicGroupUsageDto(g.group_id, g.name, "rejected"))
                .ToListAsync(ct);
            var combinedGroups = acceptedGroups
                .Concat(pendingGroups)
                .Concat(rejectedGroups)
                .ToList();

            return new TopicDetailDto(
                row.topic_id,
                row.semester_id,
                row.SemesterSeason,
                row.SemesterYear,
                row.major_id,
                row.MajorName,
                row.title,
                row.description,
                row.source,
                BuildFileDto(row.source, row.source_file_name, row.source_file_type, row.source_file_size),
                row.status,
                row.created_by,
                row.CreatedByName,
                row.CreatedByEmail,
                row.Mentors,
                combinedGroups,
                ParseSkills(row.skills),
                row.created_at);
        }

        private static IReadOnlyList<TopicGroupUsageDto> CombineGroups(
            Guid topicId,
            IReadOnlyDictionary<Guid, List<TopicGroupUsageDto>> accepted,
            IReadOnlyDictionary<Guid, List<TopicGroupUsageDto>> pending,
            IReadOnlyDictionary<Guid, List<TopicGroupUsageDto>> rejected)
        {
            var list = new List<TopicGroupUsageDto>();
            if (accepted.TryGetValue(topicId, out var acc))
                list.AddRange(acc);
            if (pending.TryGetValue(topicId, out var pend))
                list.AddRange(pend);
            if (rejected.TryGetValue(topicId, out var rej))
                list.AddRange(rej);
            return list;
        }

        public async Task<Guid?> FindSemesterIdByCodeAsync(string semesterCode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(semesterCode))
                return null;

            var code = semesterCode.Trim().ToUpperInvariant();

            // Tách phần chữ (season) + phần số (year) từ ví dụ "FALL24"
            var seasonPart = new string(code.TakeWhile(c => !char.IsDigit(c)).ToArray());
            var yearPart   = new string(code.Skip(seasonPart.Length).ToArray());

            if (!string.IsNullOrEmpty(seasonPart) &&
                !string.IsNullOrEmpty(yearPart) &&
                int.TryParse(yearPart, out var yearRaw))
            {
                var year = yearRaw < 100 ? 2000 + yearRaw : yearRaw;
                var seasonLower = seasonPart.ToLowerInvariant();

                return await _db.semesters
                    .AsNoTracking()
                    .Where(s => s.season.ToLower() == seasonLower && s.year == year)
                    .Select(s => (Guid?)s.semester_id)
                    .FirstOrDefaultAsync(ct);
            }

            // fallback: code chỉ là season (FALL / SPRING / SUMMER)
            var seasonOnly = code.ToLowerInvariant();
            return await _db.semesters
                .AsNoTracking()
                .Where(s => s.season.ToLower() == seasonOnly)
                .OrderByDescending(s => s.year)
                .Select(s => (Guid?)s.semester_id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<Guid?> FindMajorIdByNameAsync(string majorName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(majorName))
                return null;

            var normalized = majorName.Trim().ToLowerInvariant();

            return await _db.majors
                .AsNoTracking()
                .Where(m => m.major_name.ToLower() == normalized)
                .Select(m => (Guid?)m.major_id)
                .FirstOrDefaultAsync(ct);
        }

            public async Task<Guid?> GetDefaultMentorIdAsync(Guid topicId, CancellationToken ct)
        => await _db.topics.AsNoTracking()
            .Where(t => t.topic_id == topicId)
            .SelectMany(t => t.mentors)
            .Select(u => (Guid?)u.user_id)
            .FirstOrDefaultAsync(ct);

        private static IReadOnlyList<string> ParseSkills(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<string>();

            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(json);
                if (parsed is null || parsed.Count == 0)
                    return Array.Empty<string>();

                return parsed
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static TopicRegistrationFileDto? BuildFileDto(
            string? fileUrl,
            string? fileName,
            string? contentType,
            long? fileSize)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
                return null;

            return new TopicRegistrationFileDto(fileUrl, fileName, contentType, fileSize);
        }
    }
}
