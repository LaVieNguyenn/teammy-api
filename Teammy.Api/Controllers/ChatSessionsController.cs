using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Chat.Dtos;
using Teammy.Application.Chat.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/chat/sessions")]
[Authorize]
public sealed class ChatSessionsController(ChatSessionMessageService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpGet("{sessionId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> ListMessages([FromRoute] Guid sessionId, [FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken ct = default)
    {
        try
        {
            var messages = await service.ListMessagesAsync(sessionId, GetUserId(), limit, offset, ct);
            return Ok(messages);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{sessionId:guid}/messages")]
    public async Task<ActionResult> SendMessage([FromRoute] Guid sessionId, [FromBody] SendChatMessageRequest request, CancellationToken ct)
    {
        try
        {
            var msg = await service.SendMessageAsync(sessionId, GetUserId(), request, ct);
            return Ok(msg);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
