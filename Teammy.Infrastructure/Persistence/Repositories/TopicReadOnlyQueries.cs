using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
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

            return await src
                .OrderByDescending(t => t.created_at)
                .Select(t => new TopicListItemDto(
                    t.topic_id,                               // TopicId
                    t.semester_id,                            // SemesterId
                    t.semester.season,                        // SemesterSeason
                    t.semester.year,                          // SemesterYear
                    t.major_id,                               // MajorId
                    t.major != null ? t.major.major_name : null, // MajorName
                    t.title,                                  // Title
                    t.description,                            // Description
                    t.source,                                 // Source
                    t.status,                                 // Status
                    t.created_by,                             // CreatedById
                    t.created_byNavigation.display_name,      // CreatedByName
                    t.created_byNavigation.email,             // CreatedByEmail
                    t.mentors
                        .OrderBy(m => m.display_name)
                        .Select(m => new TopicMentorDto(
                            m.user_id,
                            m.display_name,
                            m.email
                        ))
                        .ToList(),                            // Mentors
                    t.created_at                              // CreatedAt
                ))
                .ToListAsync(ct);
        }

        public async Task<TopicDetailDto?> GetByIdAsync(Guid topicId, CancellationToken ct)
        {
            return await _db.topics
                .Include(t => t.semester)
                .Include(t => t.major)
                .Include(t => t.created_byNavigation)
                .Include(t => t.mentors)
                .AsNoTracking()
                .Where(t => t.topic_id == topicId)
                .Select(t => new TopicDetailDto(
                    t.topic_id,                               // TopicId
                    t.semester_id,                            // SemesterId
                    t.semester.season,                        // SemesterSeason
                    t.semester.year,                          // SemesterYear
                    t.major_id,                               // MajorId
                    t.major != null ? t.major.major_name : null, // MajorName
                    t.title,                                  // Title
                    t.description,                            // Description
                    t.source,                                 // Source
                    t.status,                                 // Status
                    t.created_by,                             // CreatedById
                    t.created_byNavigation.display_name,      // CreatedByName
                    t.created_byNavigation.email,             // CreatedByEmail
                    t.mentors
                        .OrderBy(m => m.display_name)
                        .Select(m => new TopicMentorDto(
                            m.user_id,
                            m.display_name,
                            m.email
                        ))
                        .ToList(),                            // Mentors
                    t.created_at                              // CreatedAt
                ))
                .FirstOrDefaultAsync(ct);
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
    }
}
