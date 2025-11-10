using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Topics.Dtos;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class TopicWriteRepository(AppDbContext db) : ITopicWriteRepository
{
    private static bool Valid(string s) => s is "open" or "closed" or "archived";

    public async Task<Guid> CreateAsync(CreateTopicRequest req, Guid createdBy, CancellationToken ct)
    {
        var status = string.IsNullOrWhiteSpace(req.Status) ? "open" : req.Status!.Trim().ToLowerInvariant();
        if (!Valid(status)) throw new ArgumentException("status must be open|closed|archived");

        var dup = await db.topics.AnyAsync(t => t.semester_id == req.SemesterId && t.title.ToLower() == req.Title.ToLower(), ct);
        if (dup) throw new InvalidOperationException("Topic title already exists in this semester");

        var now = DateTime.UtcNow;
        var e = new topic
        {
            topic_id    = Guid.NewGuid(),
            semester_id = req.SemesterId,
            major_id    = req.MajorId,
            title       = req.Title.Trim(),
            description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
            status      = status,
            created_by  = createdBy,
            created_at  = now,
        };

        db.topics.Add(e);
        await db.SaveChangesAsync(ct);
        return e.topic_id;
    }

    public async Task UpdateAsync(Guid topicId, UpdateTopicRequest req, CancellationToken ct)
    {
        if (!Valid(req.Status)) throw new ArgumentException("status must be open|closed|archived");

        var e = await db.topics.FirstOrDefaultAsync(x => x.topic_id == topicId, ct)
              ?? throw new KeyNotFoundException("Topic not found");

        if (!string.Equals(e.title, req.Title, StringComparison.Ordinal))
        {
            var dup = await db.topics.AnyAsync(t => t.semester_id == e.semester_id && t.topic_id != e.topic_id &&
                                                    t.title.ToLower() == req.Title.ToLower(), ct);
            if (dup) throw new InvalidOperationException("Topic title already exists in this semester");
        }

        e.title       = req.Title.Trim();
        e.description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description;
        e.status      = req.Status.Trim().ToLowerInvariant();
        e.major_id    = req.MajorId;

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid topicId, CancellationToken ct)
    {
        var e = await db.topics.FirstOrDefaultAsync(x => x.topic_id == topicId, ct)
              ?? throw new KeyNotFoundException("Topic not found");
        db.topics.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(Guid topicId, bool created)> UpsertAsync(
        Guid semesterId, string title, string? description, string status,
        Guid? majorId, Guid createdBy, CancellationToken ct)
    {
        status = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
        if (!Valid(status)) throw new ArgumentException("status must be open|closed|archived");

        var exist = await db.topics.FirstOrDefaultAsync(t =>
            t.semester_id == semesterId && t.title.ToLower() == title.ToLower(), ct);

        if (exist is null)
        {
            var id = await CreateAsync(new CreateTopicRequest(semesterId, majorId, title, description, status), createdBy, ct);
            return (id, true);
        }

        exist.description = string.IsNullOrWhiteSpace(description) ? exist.description : description;
        exist.status      = status;
        exist.major_id    = majorId ?? exist.major_id;

        await db.SaveChangesAsync(ct);
        return (exist.topic_id, false);
    }
}
