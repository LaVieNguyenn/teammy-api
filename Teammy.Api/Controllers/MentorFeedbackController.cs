using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Feedback.Dtos;
using Teammy.Application.Feedback.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/feedback")]
public sealed class MentorFeedbackController(MentorFeedbackService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> Submit([FromRoute] Guid groupId, [FromBody] SubmitGroupFeedbackRequest req, CancellationToken ct)
    {
        try
        {
            var id = await service.SubmitAsync(groupId, GetUserId(), req, ct);
            return CreatedAtAction(nameof(List), new { groupId }, new { feedbackId = id });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<GroupFeedbackDto>>> List([FromRoute] Guid groupId, CancellationToken ct)
    {
        try
        {
            var list = await service.ListAsync(groupId, GetUserId(), ct);
            return Ok(list);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
    }

    [HttpPost("{feedbackId:guid}/status")]
    [Authorize]
    public async Task<ActionResult> UpdateStatus([FromRoute] Guid groupId, [FromRoute] Guid feedbackId, [FromBody] UpdateGroupFeedbackStatusRequest req, CancellationToken ct)
    {
        try
        {
            await service.UpdateStatusAsync(groupId, feedbackId, GetUserId(), req, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPut("{feedbackId:guid}")]
    [Authorize]
    public async Task<ActionResult> Update([FromRoute] Guid groupId, [FromRoute] Guid feedbackId, [FromBody] UpdateGroupFeedbackRequest req, CancellationToken ct)
    {
        try
        {
            await service.UpdateAsync(groupId, feedbackId, GetUserId(), req, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpDelete("{feedbackId:guid}")]
    [Authorize]
    public async Task<ActionResult> Delete([FromRoute] Guid groupId, [FromRoute] Guid feedbackId, CancellationToken ct)
    {
        try
        {
            await service.DeleteAsync(groupId, feedbackId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }
}
