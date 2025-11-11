using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Groups.Services;
using Teammy.Application.Invitations.Services;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/groups")]
public sealed class GroupsController : ControllerBase
{
    private readonly GroupService _service;
    private readonly InvitationService _invitations;
    private readonly IGroupReadOnlyQueries _groupQueries;
    public GroupsController(GroupService service, InvitationService invitations, IGroupReadOnlyQueries groupQueries)
    {
        _service = service;
        _invitations = invitations;
        _groupQueries = groupQueries;
    }

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
    public async Task<ActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var g = await _service.GetGroupAsync(id, ct);
        if (g is null) return NotFound();

        // Build enriched object similar to recruitment object-only
        var members = await _service.ListActiveMembersAsync(id, ct);
        var leaderMember = members.FirstOrDefault(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase));
        var nonLeaderMembers = members.Where(m => !string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase)).ToList();

        PostSemesterDto? semesterObj = null;
        if (g.SemesterId != Guid.Empty)
        {
            var s = await _groupQueries.GetSemesterAsync(g.SemesterId, ct);
            if (s.HasValue)
            {
                var (semId, season, year, start, end, active) = s.Value;
                semesterObj = new PostSemesterDto(semId, season, year, start, end, active);
            }
        }

        PostMajorDto? majorObj = null;
        if (g.MajorId.HasValue)
        {
            var m = await _groupQueries.GetMajorAsync(g.MajorId.Value, ct);
            if (m.HasValue)
            {
                var (mid, mname) = m.Value;
                majorObj = new PostMajorDto(mid, mname);
            }
        }

        return Ok(new
        {
            id = g.Id,
            name = g.Name,
            description = g.Description,
            status = g.Status,
            maxMembers = g.MaxMembers,
            currentMembers = g.CurrentMembers,
            semester = semesterObj,
            major = majorObj,
            leader = leaderMember,
            members = nonLeaderMembers // exclude leader
        });
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

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult> My([FromQuery] Guid? semesterId, CancellationToken ct)
    {
        var userId = GetUserId();
        var list = await _service.ListMyGroupsAsync(userId, semesterId, ct);

        // Shape to object-only similar to recruitment post details
        var shaped = new List<object>(list.Count);
        foreach (var g in list)
        {
            // Semester object
            PostSemesterDto? semesterObj = null;
            var s = await _groupQueries.GetSemesterAsync(g.SemesterId, ct);
            if (s.HasValue)
            {
                var (semId, season, year, start, end, active) = s.Value;
                semesterObj = new PostSemesterDto(semId, season, year, start, end, active);
            }

            // Detail for optional fields (description/major)
            var detail = await _service.GetGroupAsync(g.GroupId, ct);

            PostMajorDto? majorObj = null;
            if (detail?.MajorId is Guid mid)
            {
                var m = await _groupQueries.GetMajorAsync(mid, ct);
                if (m.HasValue)
                {
                    var (majorId, majorName) = m.Value;
                    majorObj = new PostMajorDto(majorId, majorName);
                }
            }

            // Members (leader separated)
            var members = await _service.ListActiveMembersAsync(g.GroupId, ct);
            var leaderMember = members.FirstOrDefault(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase));
            var nonLeaderMembers = members.Where(m => !string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase)).ToList();

            shaped.Add(new
            {
                id = g.GroupId,
                name = g.Name,
                description = detail?.Description,
                status = g.Status,
                maxMembers = g.MaxMembers,
                currentMembers = g.CurrentMembers,
                role = g.Role,
                semester = semesterObj,
                major = majorObj,
                leader = leaderMember,
                members = nonLeaderMembers
            });
        }

        return Ok(shaped);
    }

    [HttpGet("{id:guid}/members")]
    [AllowAnonymous]
    public Task<IReadOnlyList<Teammy.Application.Groups.Dtos.GroupMemberDto>> Members([FromRoute] Guid id, CancellationToken ct)
        => _service.ListActiveMembersAsync(id, ct);

    public sealed record TransferLeaderRequest(Guid NewLeaderUserId);

    [HttpPost("{id:guid}/leader/transfer")]
    [Authorize]
    public async Task<ActionResult> TransferLeader([FromRoute] Guid id, [FromBody] TransferLeaderRequest req, CancellationToken ct)
    {
        try
        {
            await _service.TransferLeadershipAsync(id, GetUserId(), req.NewLeaderUserId, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("{id:guid}/close")]
    [Authorize]
    public async Task<ActionResult> Close([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await _service.CloseGroupAsync(id, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("membership")]
    [Authorize]
    public Task<Teammy.Application.Groups.Dtos.UserGroupCheckDto> CheckMembership(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? semesterId,
        [FromQuery] bool includePending = true,
        CancellationToken ct = default)
        => _service.CheckUserGroupAsync(userId ?? GetUserId(), semesterId, includePending, ct);

    [HttpPost("{id:guid}/join-requests/{reqId:guid}/accept")]
    [Authorize]
    public async Task<ActionResult> Accept([FromRoute] Guid id, [FromRoute] Guid reqId, CancellationToken ct)
    {
        try
        {
            await _service.AcceptJoinRequestAsync(id, reqId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
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
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{id:guid}/invites")]
    [Authorize]
    public async Task<ActionResult> Invite([FromRoute] Guid id, [FromBody] InviteUserRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _invitations.InviteUserAsync(id, req.UserId, GetUserId(), null, ct);
            var leaderName = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "Leader";
            return Accepted(new { invitationId = result.InvitationId, emailSent = result.EmailSent, leaderName });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.StartsWith("already_invited:"))
            {
                var idText = ex.Message.Split(':', 2)[1];
                if (Guid.TryParse(idText, out var invId))
                    return Conflict(new { code = "already_invited", invitationId = invId });
                return Conflict(new { code = "already_invited" });
            }
            if (ex.Message.StartsWith("invite_exists:"))
            {
                var parts = ex.Message.Split(':');
                Guid.TryParse(parts.ElementAtOrDefault(1), out var invId);
                var status = parts.ElementAtOrDefault(2) ?? "unknown";
                return Conflict(new { code = "invite_exists", invitationId = invId, status });
            }
            return Conflict(ex.Message);
        }
    }

    [HttpPatch("{id:guid}/members/{userId:guid}")]
    [Authorize]
    public ActionResult AssignRole([FromRoute] Guid id, [FromRoute] Guid userId)
        => StatusCode(501, "Not Implemented: internal member role not yet modeled");
}
