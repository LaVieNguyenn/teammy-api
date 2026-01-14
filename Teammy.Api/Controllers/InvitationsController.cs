using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Invitations.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/invitations")]
public sealed class InvitationsController(InvitationService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpGet]
    [Authorize]
    public Task<IReadOnlyList<Teammy.Application.Invitations.Dtos.InvitationListItemDto>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? semesterId,
        [FromQuery] Guid? majorId,
        CancellationToken ct)
        => service.ListMyInvitationsAsync(GetUserId(), status, semesterId, majorId, ct);

    [HttpPost("{id:guid}/accept")]
    [Authorize]
    public async Task<ActionResult> Accept([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await service.AcceptAsync(id, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("{id:guid}/decline")]
    [Authorize]
    public async Task<ActionResult> Decline([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await service.DeclineAsync(id, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }
}
