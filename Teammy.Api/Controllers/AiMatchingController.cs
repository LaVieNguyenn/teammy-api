using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Ai.Dtos;
using Teammy.Application.Ai.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AiMatchingController : ControllerBase
{
    private readonly AiMatchingService _service;

    public AiMatchingController(AiMatchingService service)
    {
        _service = service;
    }

    [HttpPost("recruitment-post-suggestions")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<RecruitmentPostSuggestionDto>>> SuggestRecruitmentPosts(
        [FromBody] RecruitmentPostSuggestionRequest request,
        CancellationToken ct)
    {
        var result = await _service.SuggestRecruitmentPostsForStudentAsync(GetCurrentUserId(), request, ct);
        return Ok(result);
    }

    [HttpPost("topic-suggestions")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<TopicSuggestionDto>>> SuggestTopics(
        [FromBody] TopicSuggestionRequest request,
        CancellationToken ct)
    {
        var result = await _service.SuggestTopicsForGroupAsync(GetCurrentUserId(), request, ct);
        return Ok(result);
    }

    [HttpPost("profile-post-suggestions")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ProfilePostSuggestionDto>>> SuggestProfilePosts(
        [FromBody] ProfilePostSuggestionRequest request,
        CancellationToken ct)
    {
        var result = await _service.SuggestProfilePostsForGroupAsync(GetCurrentUserId(), request, ct);
        return Ok(result);
    }

    [HttpPost("auto-assign/teams")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<AutoAssignTeamsResultDto>> AutoAssignTeams(
        [FromBody] AutoAssignTeamsRequest request,
        CancellationToken ct)
    {
        var result = await _service.AutoAssignTeamsAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("auto-assign/topic")]
    [Authorize]
    public async Task<ActionResult<AutoAssignTopicBatchResultDto>> AutoAssignTopic(
        [FromBody] AutoAssignTopicRequest request,
        CancellationToken ct)
    {
        var isAdmin = User.IsInRole("admin") || User.IsInRole("moderator");
        if (!isAdmin && (request is null || !request.GroupId.HasValue))
            return StatusCode(403, "Chỉ admin/moderator mới được phép auto assign cho toàn bộ nhóm.");

        var result = await _service.AutoAssignTopicAsync(GetCurrentUserId(), isAdmin, request, ct);
        return Ok(result);
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
}
