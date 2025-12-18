using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Ai;
using Teammy.Infrastructure.Ai.Indexing;
using Teammy.Infrastructure.Persistence;

[ApiController]
[Route("api/ai-index")]
public sealed class AiIndexController(
    IAiMatchingQueries aiQueries,
    AiGatewayClient gateway,
    AppDbContext db) : ControllerBase
{
    [HttpGet("sync-status")]
    //[Authorize(Roles="admin,moderator")]
    public async Task<ActionResult<object>> GetStatusAsync(CancellationToken ct)
    {
        var pending = await db.AiIndexOutbox
            .AsNoTracking()
            .Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new AiIndexOutboxSnapshot(
                x.Id,
                x.Type,
                x.EntityId,
                x.Action,
                x.CreatedAtUtc,
                x.RetryCount,
                x.LastError))
            .ToListAsync(ct);

        var failedCount = pending.Count(x => !string.IsNullOrWhiteSpace(x.LastError));
        return Ok(new
        {
            pendingCount = pending.Count,
            failedCount,
            pending
        });
    }

    [HttpPost("replay-failed")]
    //[Authorize(Roles="admin,moderator")]
    public async Task<ActionResult<object>> ReplayFailedAsync(CancellationToken ct)
    {
        var failed = await db.AiIndexOutbox
            .Where(x => x.ProcessedAtUtc == null && x.LastError != null)
            .ToListAsync(ct);

        foreach (var item in failed)
        {
            item.RetryCount = 0;
            item.LastError = null;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { replayed = failed.Count });
    }

    [HttpPost("rebuild")]
    //[Authorize(Roles="admin,moderator")]
    public async Task<ActionResult<object>> Rebuild([FromQuery] Guid? semesterId, [FromQuery] Guid? majorId, CancellationToken ct)
    {
        if (!semesterId.HasValue || semesterId.Value == Guid.Empty)
        {
            // Business rule: only 1 semester is active at a time, so auto-use it.
            var activeIds = await db.semesters
                .AsNoTracking()
                .Where(s => s.is_active == true)
                .Select(s => s.semester_id)
                .ToListAsync(ct);

            if (activeIds.Count == 0)
                return BadRequest(new { error = "No active semester found." });

            if (activeIds.Count > 1)
                return Problem($"Expected exactly 1 active semester, but found {activeIds.Count}.");

            semesterId = activeIds[0];
        }

        var sid = semesterId.Value;
        var topics = await aiQueries.ListTopicAvailabilityAsync(sid, majorId, ct);
        var rposts = await aiQueries.ListOpenRecruitmentPostsAsync(sid, majorId, ct);
        var pposts = await aiQueries.ListOpenProfilePostsAsync(sid, majorId, ct);

        var sem = new SemaphoreSlim(6);
        var tasks = new List<Task>();

        foreach (var t in topics)
        {
            await sem.WaitAsync(ct);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var text = $"{t.Title}\n{t.Description}\nSkills: {string.Join(", ", t.SkillNames)}\n{t.SkillsJson}";
                    await gateway.UpsertAsync(new AiGatewayUpsertRequest(
                        Type: "topic",
                        EntityId: t.TopicId.ToString(),
                        Title: t.Title,
                        Text: text,
                        SemesterId: t.SemesterId.ToString(),
                        MajorId: t.MajorId?.ToString(),
                        PointId: AiPointId.Stable("topic", t.TopicId)
                    ), ct);
                }
                finally { sem.Release(); }
            }, ct));
        }
        foreach (var p in rposts)
{
    await sem.WaitAsync(ct);
    tasks.Add(Task.Run(async () =>
    {
        try
        {
            var text =
$@"{p.Title}
{p.Description}
Major: {p.MajorName}
Group: {p.GroupName}
PositionNeeded: {p.PositionNeeded}
RequiredSkills: {p.RequiredSkills}";

            await gateway.UpsertAsync(new AiGatewayUpsertRequest(
                Type: "recruitment_post",
                EntityId: p.PostId.ToString(),
                Title: p.Title,
                Text: text,
                SemesterId: p.SemesterId.ToString(),
                MajorId: p.MajorId?.ToString(),
                PointId: AiPointId.Stable("recruitment_post", p.PostId)
            ), ct);
        }
        finally { sem.Release(); }
    }, ct));
}

foreach (var p in pposts)
{
    await sem.WaitAsync(ct);
    tasks.Add(Task.Run(async () =>
    {
        try
        {
            var text =
$@"{p.Title}
{p.Description}
PrimaryRole: {p.PrimaryRole}
SkillsText: {p.SkillsText}
SkillsJson: {p.SkillsJson}";

            await gateway.UpsertAsync(new AiGatewayUpsertRequest(
                Type: "profile_post",
                EntityId: p.PostId.ToString(),
                Title: p.Title,
                Text: text,
                SemesterId: p.SemesterId.ToString(),
                MajorId: p.MajorId?.ToString(),
                PointId: AiPointId.Stable("profile_post", p.PostId)
            ), ct);
        }
        finally { sem.Release(); }
    }, ct));
}
        await Task.WhenAll(tasks);
        return Ok(new { semesterId = sid, topics = topics.Count, recruitmentPosts = rposts.Count, profilePosts = pposts.Count });
    }
    private sealed record AiIndexOutboxSnapshot(
        long Id,
        string Type,
        Guid EntityId,
        AiIndexAction Action,
        DateTime CreatedAtUtc,
        int RetryCount,
        string? LastError);
}
