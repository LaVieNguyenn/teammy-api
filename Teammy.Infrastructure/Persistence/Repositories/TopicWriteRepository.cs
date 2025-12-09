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
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories
{
    public sealed class TopicWriteRepository : ITopicWriteRepository
    {
        private readonly AppDbContext _db;

        public TopicWriteRepository(AppDbContext db)
        {
            _db = db;
        }

        private static bool ValidStatus(string s) => s is "open" or "closed" or "archived";

        public async Task<Guid> CreateAsync(CreateTopicRequest req, Guid createdBy, CancellationToken ct)
        {
            var status = NormalizeStatus(req.Status);
            var source = NormalizeSource(req.Source);

            var titleTrim = req.Title.Trim();
            var dup = await _db.topics.AsNoTracking()
                .AnyAsync(t =>
                    t.semester_id == req.SemesterId &&
                    t.title.ToLower() == titleTrim.ToLower(), ct);

            if (dup)
                throw new InvalidOperationException("Topic title already exists in this semester.");

            var entity = new topic
            {
                topic_id    = Guid.NewGuid(),
                semester_id = req.SemesterId,
                major_id    = req.MajorId,
                title       = titleTrim,
                description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
                source      = source,
                skills      = SerializeSkills(req.Skills),
                status      = status,
                created_by  = createdBy,
                created_at  = DateTime.UtcNow
            };

            _db.topics.Add(entity);
            await _db.SaveChangesAsync(ct);
            return entity.topic_id;
        }

        public async Task UpdateAsync(Guid topicId, UpdateTopicRequest req, CancellationToken ct)
        {
            var status = NormalizeStatus(req.Status);

            var entity = await _db.topics
                .FirstOrDefaultAsync(x => x.topic_id == topicId, ct)
                ?? throw new KeyNotFoundException("Topic not found");

            var titleTrim = req.Title.Trim();
            if (!string.Equals(entity.title, titleTrim, StringComparison.Ordinal))
            {
                var dup = await _db.topics.AsNoTracking()
                    .AnyAsync(t =>
                        t.semester_id == entity.semester_id &&
                        t.topic_id != entity.topic_id &&
                        t.title.ToLower() == titleTrim.ToLower(), ct);

                if (dup)
                    throw new InvalidOperationException("Topic title already exists in this semester.");
            }

            entity.title       = titleTrim;
            entity.description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description;
            entity.source      = NormalizeSource(req.Source);
            entity.status      = status;
            entity.major_id    = req.MajorId;
            if (req.Skills is not null)
                entity.skills = SerializeSkills(req.Skills);

            await _db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(Guid topicId, CancellationToken ct)
        {
            var entity = await _db.topics
                .FirstOrDefaultAsync(x => x.topic_id == topicId, ct);

            if (entity is null) return;

            _db.topics.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }

        public async Task SetStatusAsync(Guid topicId, string status, CancellationToken ct)
        {
            var normalized = NormalizeStatus(status);
            var entity = await _db.topics.FirstOrDefaultAsync(x => x.topic_id == topicId, ct)
                ?? throw new KeyNotFoundException("Topic not found");
            entity.status = normalized;
            await _db.SaveChangesAsync(ct);
        }

        public async Task<(Guid topicId, bool created)> UpsertAsync(
            Guid semesterId,
            string title,
            string? description,
            string status,
            Guid? majorId,
            string? source,
            string? sourceFileName,
            string? sourceFileType,
            long? sourceFileSize,
            IReadOnlyList<string>? skills,
            Guid createdBy,
            CancellationToken ct)
        {
            status = NormalizeStatus(status);
            var titleTrim = title.Trim();
            var normalizedSource = NormalizeSource(source);
            var serializedSkills = SerializeSkills(skills);

            var exist = await _db.topics
                .FirstOrDefaultAsync(t =>
                    t.semester_id == semesterId &&
                    t.title.ToLower() == titleTrim.ToLower(), ct);

            if (exist is null)
            {
                var entity = new topic
                {
                    topic_id    = Guid.NewGuid(),
                    semester_id = semesterId,
                    major_id    = majorId,
                    title       = titleTrim,
                    description = string.IsNullOrWhiteSpace(description) ? null : description,
                    source      = normalizedSource,
                    source_file_name = sourceFileName,
                    source_file_type = sourceFileType,
                    source_file_size = sourceFileSize,
                    skills      = serializedSkills,
                    status      = status,
                    created_by  = createdBy,
                    created_at  = DateTime.UtcNow
                };

                _db.topics.Add(entity);
                await _db.SaveChangesAsync(ct);
                return (entity.topic_id, true);
            }

            if (!string.IsNullOrWhiteSpace(description))
                exist.description = description;

            if (normalizedSource is not null)
            {
                exist.source = normalizedSource;
                exist.source_file_name = sourceFileName;
                exist.source_file_type = sourceFileType;
                exist.source_file_size = sourceFileSize;
            }

            exist.status = status;
            if (majorId.HasValue)
                exist.major_id = majorId;

            if (serializedSkills is not null)
                exist.skills = serializedSkills;

            await _db.SaveChangesAsync(ct);
            return (exist.topic_id, false);
        }

        private static string NormalizeStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return "open";
            var s = status.Trim().ToLowerInvariant();
            if (!ValidStatus(s))
                throw new ArgumentException("Status must be open|closed|archived.");
            return s;
        }

        private static string? SerializeSkills(IEnumerable<string>? skills)
        {
            if (skills is null)
                return null;

            var normalized = skills
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return normalized.Count == 0
                ? null
                : JsonSerializer.Serialize(normalized);
        }

        private static string? NormalizeSource(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var trimmed = raw.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Source must be a valid http(s) link.");

            return uri.ToString();
        }
    }
}
