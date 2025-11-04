using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Groups.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/groups")]
public sealed class GroupsController : ControllerBase
{
    private readonly GroupService _service;
    public GroupsController(GroupService service) => _service = service;

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> Create([FromBody] CreateGroupRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _service.CreateGroupAsync(GetUserId(), req, ct);
            return CreatedAtAction(nameof(GetById), new { id }, new { id });
        }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<GroupSummaryDto>> List([FromQuery] string? status, [FromQuery] Guid? majorId, [FromQuery] Guid? topicId, CancellationToken ct)
        => _service.ListGroupsAsync(status, majorId, topicId, ct);

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<GroupDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var g = await _service.GetGroupAsync(id, ct);
        return g is null ? NotFound() : Ok(g);
    }

    [HttpPost("{id:guid}/join-requests")]
    [Authorize]
    public async Task<ActionResult> Apply([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await _service.ApplyToGroupAsync(id, GetUserId(), ct);
            return Accepted();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpDelete("{id:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult> Leave([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await _service.LeaveGroupAsync(id, GetUserId(), ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    // Leader-only
    [HttpGet("{id:guid}/join-requests")]
    [Authorize]
    public Task<IReadOnlyList<JoinRequestDto>> ListJoinRequests([FromRoute] Guid id, CancellationToken ct)
        => _service.ListJoinRequestsAsync(id, GetUserId(), ct);

    [HttpPost("{id:guid}/join-requests/{reqId:guid}/accept")]
    [Authorize]
    public async Task<ActionResult> Accept([FromRoute] Guid id, [FromRoute] Guid reqId, CancellationToken ct)
    {
        try
        {
            await _service.AcceptJoinRequestAsync(id, reqId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("{id:guid}/join-requests/{reqId:guid}/reject")]
    [Authorize]
    public async Task<ActionResult> Reject([FromRoute] Guid id, [FromRoute] Guid reqId, CancellationToken ct)
    {
        try
        {
            await _service.RejectJoinRequestAsync(id, reqId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{id:guid}/invites")]
    [Authorize]
    public async Task<ActionResult> Invite([FromRoute] Guid id, [FromBody] InviteUserRequest req, CancellationToken ct)
    {
        try
        {
            await _service.InviteUserAsync(id, req.UserId, GetUserId(), ct);
            return Accepted();
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPatch("{id:guid}/members/{userId:guid}")]
    [Authorize]
    public ActionResult AssignRole([FromRoute] Guid id, [FromRoute] Guid userId)
        => StatusCode(501, "Not Implemented: internal member role not yet modeled");
}

