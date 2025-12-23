using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Chat.Dtos;
using Teammy.Application.Chat.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/chat/conversations")]
[Authorize]
public sealed class ChatConversationsController(ChatConversationService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpGet]
    public Task<IReadOnlyList<ConversationSummaryDto>> List(CancellationToken ct)
        => service.ListMyConversationsAsync(GetUserId(), ct);

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateDirectConversationRequest request, CancellationToken ct)
    {
        if (request is null || request.UserId == Guid.Empty)
            return BadRequest("userId is required");
        try
        {
            var sessionId = await service.CreateDirectConversationAsync(GetUserId(), request.UserId, ct);
            return Ok(new { sessionId });
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{sessionId:guid}/pin")]
    public async Task<ActionResult> Pin([FromRoute] Guid sessionId, [FromBody] PinChatSessionRequest request, CancellationToken ct)
    {
        try
        {
            await service.SetPinAsync(sessionId, GetUserId(), request?.Pin ?? true, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
