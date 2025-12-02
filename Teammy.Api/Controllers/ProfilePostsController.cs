using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.Posts.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/profile-posts")]
public sealed class ProfilePostsController(ProfilePostService service, IConfiguration cfg, IGroupReadOnlyQueries groupQueries) : ControllerBase
{
    private readonly bool _objectOnlyDefault = string.Equals(cfg["Api:Posts:DefaultShape"], "object", StringComparison.OrdinalIgnoreCase);
    private readonly IGroupReadOnlyQueries _groupQueries = groupQueries;
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }
    private Guid? TryGetUserId()
    {
        var sub = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> Create([FromBody] CreateProfilePostRequest req, CancellationToken ct)
    {
        try
        {
            var id = await service.CreateAsync(GetUserId(), req, ct);
            return CreatedAtAction(nameof(GetById), new { id }, new { id });
        }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> List([FromQuery] string? skills, [FromQuery] Guid? majorId, [FromQuery] string? status, [FromQuery] string? expand, [FromQuery] string? shape, CancellationToken ct)
    {
        var exp = ParseExpand(expand);
        var objectOnly = _objectOnlyDefault;
        if (!string.IsNullOrWhiteSpace(shape))
        {
            objectOnly = string.Equals(shape, "object", StringComparison.OrdinalIgnoreCase);
        }
        if (objectOnly)
        {
            exp |= ExpandOptions.Semester | ExpandOptions.Major | ExpandOptions.User;
        }
        var currentUserId = TryGetUserId();
        var items = await service.ListAsync(skills, majorId, status, exp, currentUserId, ct);
        if (!objectOnly) return Ok(items);

        // Build sequentially to avoid concurrent DbContext usage
        var shaped = new List<object>(items.Count);
        foreach (var d in items)
        {
            Guid[]? memberUserIds = null;
            Guid? userGroupId = null;
            if (d.User?.UserId is Guid uid)
            {
                var check = await _groupQueries.CheckUserGroupAsync(uid, d.SemesterId, includePending: false, ct);
                if (check.HasGroup && check.GroupId.HasValue)
                {
                    userGroupId = check.GroupId.Value;
                    var members = await _groupQueries.ListActiveMembersAsync(userGroupId.Value, ct);
                    memberUserIds = members.Select(m => m.UserId).ToArray();
                }
            }
            shaped.Add(new
            {
                id = d.Id,
                type = d.Type,
                status = d.Status,
                title = d.Title,
                description = d.Description,
                position_needed = d.Skills,
                createdAt = d.CreatedAt,
                hasApplied = d.HasApplied,
                myApplicationId = d.MyApplicationId,
                myApplicationStatus = d.MyApplicationStatus,
                semester = d.Semester,
                user = d.User,
                major = d.Major,
                userGroup = userGroupId is null ? null : new { groupId = userGroupId.Value, memberUserIds }
            });
        }
        return Ok(shaped);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProfilePostDetailDto>> GetById([FromRoute] Guid id, [FromQuery] string? expand, [FromQuery] string? shape, CancellationToken ct)
    {
        var exp = ParseExpand(expand);
        var objectOnly = _objectOnlyDefault;
        if (!string.IsNullOrWhiteSpace(shape))
        {
            objectOnly = string.Equals(shape, "object", StringComparison.OrdinalIgnoreCase);
        }
        if (objectOnly)
        {
            exp |= ExpandOptions.Semester | ExpandOptions.Major | ExpandOptions.User;
        }
        var currentUserId = TryGetUserId();
        var d = await service.GetAsync(id, exp, currentUserId, ct);
        if (d is null) return NotFound();
        if (!objectOnly) return Ok(d);
        Guid[]? memberUserIds = null;
        Guid? userGroupId = null;
        if (d.User?.UserId is Guid uid)
        {
            var check = await _groupQueries.CheckUserGroupAsync(uid, d.SemesterId, includePending: false, ct);
            if (check.HasGroup && check.GroupId.HasValue)
            {
                userGroupId = check.GroupId.Value;
                var members = await _groupQueries.ListActiveMembersAsync(userGroupId.Value, ct);
                memberUserIds = members.Select(m => m.UserId).ToArray();
            }
        }
        return Ok(new
        {
            id = d.Id,
            type = d.Type,
            status = d.Status,
            title = d.Title,
            description = d.Description,
            position_needed = d.Skills,
            createdAt = d.CreatedAt,
            hasApplied = d.HasApplied,
            myApplicationId = d.MyApplicationId,
            myApplicationStatus = d.MyApplicationStatus,
            semester = d.Semester,
            user = d.User,
            major = d.Major,
            userGroup = userGroupId is null ? null : new { groupId = userGroupId.Value, memberUserIds }
        });
    }

    [HttpPost("{id:guid}/invites")]
    [Authorize]
    public async Task<ActionResult> Invite([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await service.InviteAsync(id, GetUserId(), ct);
            return Accepted();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("my/invitations")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ProfilePostInvitationDto>>> MyInvitations([FromQuery] string? status, CancellationToken ct)
    {
        var items = await service.ListInvitationsAsync(GetUserId(), status, ct);
        return Ok(items);
    }

    [HttpPost("{postId:guid}/invitations/{candidateId:guid}/accept")]
    [Authorize]
    public async Task<ActionResult> AcceptInvitation([FromRoute] Guid postId, [FromRoute] Guid candidateId, CancellationToken ct)
    {
        try
        {
            await service.AcceptInvitationAsync(postId, candidateId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{postId:guid}/invitations/{candidateId:guid}/reject")]
    [Authorize]
    public async Task<ActionResult> RejectInvitation([FromRoute] Guid postId, [FromRoute] Guid candidateId, CancellationToken ct)
    {
        try
        {
            await service.RejectInvitationAsync(postId, candidateId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private static Teammy.Application.Posts.Dtos.ExpandOptions ParseExpand(string? e)
    {
        var opt = Teammy.Application.Posts.Dtos.ExpandOptions.None;
        if (string.IsNullOrWhiteSpace(e)) return opt;
        foreach (var part in e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "semester": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.Semester; break;
                case "major": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.Major; break;
                case "user": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.User; break;
            }
        }
        return opt;
    }
}
