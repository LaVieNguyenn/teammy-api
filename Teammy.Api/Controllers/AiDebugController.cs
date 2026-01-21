using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Ai.Dtos;
using Teammy.Application.Ai.Services;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/ai/debug")]
public sealed class AiDebugController : ControllerBase
{
    private readonly AiMatchingService _service;
    private readonly IAiGatewayTraceStore _traceStore;

    public AiDebugController(AiMatchingService service, IAiGatewayTraceStore traceStore)
    {
        _service = service;
        _traceStore = traceStore;
    }

    // Runs the same suggestion flow, but also returns the AI gateway request/response.
    [HttpPost("recruitment-post-suggestions")]
    [Authorize]
    public async Task<ActionResult<object>> DebugSuggestRecruitmentPosts(
        [FromBody] RecruitmentPostSuggestionRequest request,
        CancellationToken ct,
        [FromQuery] int takeTraces = 10)
    {
        _traceStore.Clear();

        var userId = GetCurrentUserId();
        var suggestions = await _service.SuggestRecruitmentPostsForStudentAsync(userId, request, ct);

        return Ok(new
        {
            suggestions,
            aiGatewayTraces = _traceStore.GetRecent(takeTraces)
        });
    }

    [HttpPost("profile-post-suggestions")]
    [Authorize]
    public async Task<ActionResult<object>> DebugSuggestProfilePosts(
        [FromBody] ProfilePostSuggestionRequest request,
        CancellationToken ct,
        [FromQuery] int takeTraces = 10)
    {
        _traceStore.Clear();

        var userId = GetCurrentUserId();
        var suggestions = await _service.SuggestProfilePostsForGroupAsync(userId, request, ct);

        return Ok(new
        {
            suggestions,
            aiGatewayTraces = _traceStore.GetRecent(takeTraces)
        });
    }

    [HttpPost("topic-suggestions")]
    [Authorize]
    public async Task<ActionResult<object>> DebugSuggestTopics(
        [FromBody] TopicSuggestionRequest request,
        CancellationToken ct,
        [FromQuery] int takeTraces = 10)
    {
        _traceStore.Clear();

        var userId = GetCurrentUserId();
        var suggestions = await _service.SuggestTopicsForGroupAsync(userId, request, ct);

        return Ok(new
        {
            suggestions,
            aiGatewayTraces = _traceStore.GetRecent(takeTraces)
        });
    }

    [HttpGet("traces")]
    [Authorize(Roles = "admin,moderator")]
    public ActionResult<object> GetRecentTraces([FromQuery] int take = 20)
    {
        return Ok(new
        {
            aiGatewayTraces = _traceStore.GetRecent(take)
        });
    }

    private Guid GetCurrentUserId()
    {
        // Matches style used in other controllers: user id in NameIdentifier.
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
