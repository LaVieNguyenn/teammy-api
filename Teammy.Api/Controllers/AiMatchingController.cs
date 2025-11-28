using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    public async Task<ActionResult<AiResponse<IReadOnlyList<RecruitmentPostSuggestionDto>>>> SuggestRecruitmentPosts(
        [FromBody] RecruitmentPostSuggestionRequest request,
        CancellationToken ct)
    {
        return await HandleAiRequestAsync(() =>
            _service.SuggestRecruitmentPostsForStudentAsync(GetCurrentUserId(), request, ct));
    }

    [HttpPost("topic-suggestions")]
    [Authorize]
    public async Task<ActionResult<AiResponse<IReadOnlyList<TopicSuggestionDto>>>> SuggestTopics(
        [FromBody] TopicSuggestionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return await HandleAiRequestAsync(() =>
            _service.SuggestTopicsForGroupAsync(GetCurrentUserId(), request, ct));
    }

    [HttpPost("profile-post-suggestions")]
    [Authorize]
    public async Task<ActionResult<AiResponse<IReadOnlyList<ProfilePostSuggestionDto>>>> SuggestProfilePosts(
        [FromBody] ProfilePostSuggestionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return await HandleAiRequestAsync(() =>
            _service.SuggestProfilePostsForGroupAsync(GetCurrentUserId(), request, ct));
    }

    [HttpPost("auto-assign/teams")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<AiResponse<AutoAssignTeamsResultDto>>> AutoAssignTeams(
        [FromBody] AutoAssignTeamsRequest request,
        CancellationToken ct)
    {
        return await HandleAiRequestAsync(() => _service.AutoAssignTeamsAsync(request, ct));
    }

    [HttpPost("auto-assign/topic")]
    [Authorize]
    public async Task<ActionResult<AiResponse<AutoAssignTopicBatchResultDto>>> AutoAssignTopic(
        [FromBody] AutoAssignTopicRequest request,
        CancellationToken ct)
    {
        var isAdmin = User.IsInRole("admin") || User.IsInRole("moderator");
        if (!isAdmin && (request is null || !request.GroupId.HasValue))
            return StatusCode(StatusCodes.Status403Forbidden,
                AiResponse<AutoAssignTopicBatchResultDto>.FromError("Chỉ admin/moderator mới được phép auto assign cho toàn bộ nhóm."));

        return await HandleAiRequestAsync(() =>
            _service.AutoAssignTopicAsync(GetCurrentUserId(), isAdmin, request, ct));
    }

    private async Task<ActionResult<AiResponse<T>>> HandleAiRequestAsync<T>(Func<Task<T>> action)
    {
        try
        {
            var data = await action();
            return Ok(AiResponse<T>.FromSuccess(data));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, AiResponse<T>.FromError(ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return Ok(AiResponse<T>.FromError(ex.Message));
        }
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
