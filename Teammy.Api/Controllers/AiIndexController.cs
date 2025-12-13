using Microsoft.AspNetCore.Mvc;
using Teammy.Api.Services;
using Teammy.Application.Common.Interfaces;

[ApiController]
[Route("api/ai-index")]
public sealed class AiIndexController(IAiMatchingQueries aiQueries, AiGatewayClient gateway) : ControllerBase
{
    [HttpPost("rebuild")]
    //[Authorize(Roles="admin,moderator")]
    public async Task<ActionResult<object>> Rebuild(Guid semesterId, Guid? majorId, CancellationToken ct)
    {
        var topics = await aiQueries.ListTopicAvailabilityAsync(semesterId, majorId, ct);
        var rposts = await aiQueries.ListOpenRecruitmentPostsAsync(semesterId, majorId, ct);
        var pposts = await aiQueries.ListOpenProfilePostsAsync(semesterId, majorId, ct);

        // Giới hạn concurrency để nhanh mà không “đập” laptop
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
                        PointId: StablePointId("topic", t.TopicId)
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
                PointId: StablePointId("recruitment_post", p.PostId)
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
                PointId: StablePointId("profile_post", p.PostId)
            ), ct);
        }
        finally { sem.Release(); }
    }, ct));
}



        await Task.WhenAll(tasks);
        return Ok(new { topics = topics.Count, recruitmentPosts = rposts.Count, profilePosts = pposts.Count });
    }

static string StablePointId(string type, Guid entityId)
{
    using var md5 = System.Security.Cryptography.MD5.Create();
    var bytes = System.Text.Encoding.UTF8.GetBytes($"{type}:{entityId}");
    var hash = md5.ComputeHash(bytes); // 16 bytes
    return new Guid(hash).ToString("N");
}
}
