using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Api.Contracts.Common;
using Teammy.Api.Contracts.Groups;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Groups;
using Teammy.Application.Groups.ReadModels;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/groups")]
public sealed class GroupsController : ControllerBase
{
    private readonly IGroupService _groups;
    public GroupsController(IGroupService groups) => _groups = groups;

    [HttpPost]
    [Authorize(Roles = "student")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest req, CancellationToken ct)
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(uid, out var actorId)) return Unauthorized();
        var r = await _groups.CreateAsync(req.TermId, req.Name, req.Capacity, req.TopicId, req.Description, req.TechStack, req.GithubUrl, actorId, ct);
        if (!r.Ok) return StatusCode(r.StatusCode, new ApiResponse(false, r.Message, null, r.StatusCode));
        var id = Guid.Parse(r.Message!);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var vm = await _groups.GetByIdAsync(id, ct);
        if (vm is null) return NotFound();
        return Ok(Map(vm));
    }

    [HttpGet("open")]
    [ProducesResponseType(typeof(PagedResponse<GroupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Open([FromQuery] Guid termId, [FromQuery] Guid? topicId, [FromQuery] Guid? departmentId, [FromQuery] Guid? majorId, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        if (termId == Guid.Empty) return BadRequest(new { error = "TERM_ID_REQUIRED" });
        var res = await _groups.ListOpenAsync(termId, topicId, departmentId, majorId, q, page, size, ct);
        return Ok(new PagedResponse<GroupDto>(res.Total, res.Page, res.Size, res.Items.Select(Map).ToList()));
    }

    [HttpPost("{id:guid}/join")]
    [Authorize(Roles = "student")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Join([FromRoute] Guid id, CancellationToken ct)
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(uid, out var userId)) return Unauthorized();
        var r = await _groups.JoinAsync(id, userId, ct);
        return StatusCode(r.StatusCode, new ApiResponse(r.Ok, r.Message, null, r.StatusCode));
    }

    [HttpDelete("{id:guid}/leave")]
    [Authorize(Roles = "student")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Leave([FromRoute] Guid id, CancellationToken ct)
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(uid, out var userId)) return Unauthorized();
        var r = await _groups.LeaveAsync(id, userId, ct);
        if (!r.Ok) return StatusCode(r.StatusCode, new ApiResponse(false, r.Message, null, r.StatusCode));
        return NoContent();
    }

    [HttpGet("{id:guid}/members/pending")]
    [Authorize(Roles = "student")]
    [ProducesResponseType(typeof(IEnumerable<PendingMemberDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending([FromRoute] Guid id, CancellationToken ct)
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(uid, out var leaderId)) return Unauthorized();
        var items = await _groups.GetPendingMembersAsync(id, leaderId, ct);
        return Ok(items.Select(m => new PendingMemberDto { UserId = m.UserId, DisplayName = m.DisplayName, Email = m.Email, JoinedAt = m.JoinedAt }));
    }
    [HttpPost("{id:guid}/members/{userId:guid}/accept")]
    [Authorize(Roles = "student")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept([FromRoute] Guid id, [FromRoute] Guid userId, CancellationToken ct)
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(uid, out var leaderId)) return Unauthorized();
        var r = await _groups.AcceptAsync(id, leaderId, userId, ct);
        return StatusCode(r.StatusCode, new ApiResponse(r.Ok, r.Message, null, r.StatusCode));
    }
    [HttpPost("{id:guid}/members/{userId:guid}/reject")]
    [Authorize(Roles = "student")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject([FromRoute] Guid id, [FromRoute] Guid userId, CancellationToken ct)
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(uid, out var leaderId)) return Unauthorized();
        var r = await _groups.RejectAsync(id, leaderId, userId, ct);
        return StatusCode(r.StatusCode, new ApiResponse(r.Ok, r.Message, null, r.StatusCode));
    }

    private static GroupDto Map(GroupReadModel m) => new()
    {
        Id = m.Id,
        TermId = m.TermId,
        TopicId = m.TopicId,
        TopicTitle = m.TopicTitle,
        TopicCode = m.TopicCode,
        Name = m.Name,
        Capacity = m.Capacity,
        Members = m.Members,
        Status = m.Status,
        CreatedAt = m.CreatedAt
    };
}

