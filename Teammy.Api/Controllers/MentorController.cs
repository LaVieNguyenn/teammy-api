using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Api.Contracts.Common;
using Teammy.Api.Contracts.Mentor;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Mentors;
using Teammy.Application.Mentors.ReadModels;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/mentor")]
public class MentorController : ControllerBase
{
    private readonly IMentorService _mentor;
    public MentorController(IMentorService mentor) => _mentor = mentor;

    // -------- Discovery --------
    [HttpGet("groups/open")]
    [ProducesResponseType(typeof(PagedResponse<OpenGroupDto>), 200)]
    public async Task<IActionResult> ListOpenGroups([FromQuery] Guid termId, [FromQuery] Guid? department, [FromQuery] string? topic, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var vm = await _mentor.ListOpenGroupsAsync(termId, department, topic, page, size, ct);
        return Ok(new PagedResponse<OpenGroupDto>(
            vm.Total, vm.Page, vm.Size,
            vm.Items.Select(Map).ToList()
        ));
    }

    [HttpPost("groups/{groupId:guid}/assign")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 409)]
    public async Task<IActionResult> SelfAssign([FromRoute] Guid groupId, CancellationToken ct = default)
    {
        var mentorId = GetUserId();
        var r = await _mentor.SelfAssignAsync(groupId, mentorId, ct);
        return StatusCode(r.StatusCode, new ApiResponse(r.Ok, r.Message, null, r.StatusCode));
    }

    [HttpDelete("groups/{groupId:guid}/assign")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> Unassign([FromRoute] Guid groupId, CancellationToken ct = default)
    {
        var mentorId = GetUserId();
        var ok = await _mentor.UnassignAsync(groupId, mentorId, ct);
        if (!ok) return NotFound(new ApiResponse(false, "You are not mentor of this group/topic", null, 404));
        return NoContent();
    }

    // -------- Dashboard --------
    [HttpGet("groups")]
    [ProducesResponseType(typeof(IEnumerable<AssignedGroupDto>), 200)]
    public async Task<IActionResult> GetAssignedGroups(CancellationToken ct = default)
    {
        var mentorId = GetUserId();
        var items = await _mentor.GetAssignedGroupsAsync(mentorId, ct);
        return Ok(items.Select(Map));
    }

    // -------- Profile --------
    [HttpGet("me")]
    [ProducesResponseType(typeof(MentorProfileDto), 200)]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct = default)
    {
        var mentorId = GetUserId();
        var vm = await _mentor.GetMyProfileAsync(mentorId, ct);
        if (vm is null) return NotFound();
        return Ok(Map(vm));
    }

    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 501)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateMentorProfileRequest req, CancellationToken ct = default)
    {
        var mentorId = GetUserId();
        var r = await _mentor.UpdateMyProfileAsync(mentorId, req.Bio, req.Skills, req.Availability, ct);
        return StatusCode(r.StatusCode, new ApiResponse(r.Ok, r.Message, null, r.StatusCode));
    }

    // --- mapping ---
    private static OpenGroupDto Map(OpenGroupReadModel m) => new(m.GroupId, m.Name, m.Status, m.Capacity, m.TopicId, m.TopicTitle, m.TopicCode);
    private static AssignedGroupDto Map(AssignedGroupReadModel m) => new(m.GroupId, m.Name, m.Status, m.Capacity, m.TopicId, m.TopicTitle, m.TopicCode);
    private static MentorProfileDto Map(MentorProfileReadModel m) => new(m.Id, m.DisplayName, m.Email, m.Skills, m.Bio, m.Availability);

    // --- helpers ---
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var g1)) return g1;
        if (Request.Headers.TryGetValue("X-User-Id", out var hdr) && Guid.TryParse(hdr.ToString(), out var g2)) return g2;
        return Guid.Empty;
    }
}
