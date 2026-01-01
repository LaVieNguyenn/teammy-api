using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Teammy.Application.ProjectTracking.Dtos;
using Teammy.Application.ProjectTracking.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/tracking")]
public sealed class ProjectTrackingController(ProjectTrackingService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id)) throw new UnauthorizedAccessException("Invalid token");
        return id;
    }

    // Backlog
    [HttpGet("backlog")]
    [Authorize]
    public Task<IReadOnlyList<BacklogItemVm>> ListBacklog(Guid groupId, CancellationToken ct)
        => service.ListBacklogAsync(groupId, GetUserId(), ct);

    [HttpPost("backlog")]
    [Authorize]
    public async Task<ActionResult<Guid>> CreateBacklogItem(Guid groupId, [FromBody] CreateBacklogItemRequest req, CancellationToken ct)
        => Ok(await service.CreateBacklogItemAsync(groupId, GetUserId(), req, ct));

    [HttpPut("backlog/{backlogItemId:guid}")]
    [Authorize]
    public Task UpdateBacklogItem(Guid groupId, Guid backlogItemId, [FromBody] UpdateBacklogItemRequest req, CancellationToken ct)
        => service.UpdateBacklogItemAsync(groupId, backlogItemId, GetUserId(), req, ct);

    [HttpDelete("backlog/{backlogItemId:guid}")]
    [Authorize]
    public Task ArchiveBacklogItem(Guid groupId, Guid backlogItemId, CancellationToken ct)
        => service.ArchiveBacklogItemAsync(groupId, backlogItemId, GetUserId(), ct);

    [HttpPost("backlog/{backlogItemId:guid}/promote")]
    [Authorize]
    public async Task<ActionResult<Guid>> PromoteBacklogItem(Guid groupId, Guid backlogItemId, [FromBody] PromoteBacklogItemRequest req, CancellationToken ct)
        => Ok(await service.PromoteBacklogItemAsync(groupId, backlogItemId, GetUserId(), req, ct));

    // Milestones
    [HttpGet("milestones")]
    [Authorize]
    public Task<IReadOnlyList<MilestoneVm>> ListMilestones(Guid groupId, CancellationToken ct)
        => service.ListMilestonesAsync(groupId, GetUserId(), ct);

    [HttpPost("milestones")]
    [Authorize]
    public async Task<ActionResult<Guid>> CreateMilestone(Guid groupId, [FromBody] CreateMilestoneRequest req, CancellationToken ct)
        => Ok(await service.CreateMilestoneAsync(groupId, GetUserId(), req, ct));

    [HttpPut("milestones/{milestoneId:guid}")]
    [Authorize]
    public Task UpdateMilestone(Guid groupId, Guid milestoneId, [FromBody] UpdateMilestoneRequest req, CancellationToken ct)
        => service.UpdateMilestoneAsync(groupId, milestoneId, GetUserId(), req, ct);

    [HttpDelete("milestones/{milestoneId:guid}")]
    [Authorize]
    public Task DeleteMilestone(Guid groupId, Guid milestoneId, CancellationToken ct)
        => service.DeleteMilestoneAsync(groupId, milestoneId, GetUserId(), ct);

    [HttpPost("milestones/{milestoneId:guid}/items")]
    [Authorize]
    public Task AssignMilestoneItems(Guid groupId, Guid milestoneId, [FromBody] AssignMilestoneItemsRequest req, CancellationToken ct)
        => service.AssignMilestoneItemsAsync(groupId, milestoneId, GetUserId(), req, ct);

    [HttpDelete("milestones/{milestoneId:guid}/items/{backlogItemId:guid}")]
    [Authorize]
    public Task RemoveMilestoneItem(Guid groupId, Guid milestoneId, Guid backlogItemId, CancellationToken ct)
        => service.RemoveMilestoneItemAsync(groupId, milestoneId, backlogItemId, GetUserId(), ct);

    // Reports
    [HttpGet("reports/project")]
    [Authorize]
    public Task<ProjectReportVm> GetProjectReport(Guid groupId, [FromQuery] Guid? milestoneId, CancellationToken ct)
        => service.GetProjectReportAsync(groupId, GetUserId(), milestoneId, ct);

    [HttpGet("scores")]
    [Authorize]
    public Task<MemberScoreReportVm> GetMemberScores(Guid groupId, [FromQuery] MemberScoreQuery req, CancellationToken ct)
        => service.GetMemberScoresAsync(groupId, GetUserId(), req, ct);

    // Overdue Actions
    [HttpGet("milestones/{milestoneId:guid}/overdue-actions")]
    [Authorize]
    public async Task<ActionResult<MilestoneOverdueActionsVm>> GetMilestoneOverdueActions(Guid groupId, Guid milestoneId, CancellationToken ct)
    {
        var result = await service.GetMilestoneOverdueActionsAsync(groupId, milestoneId, GetUserId(), ct);
        if (result is null)
            return NotFound("Milestone not found");
        return Ok(result);
    }

    [HttpPost("milestones/{milestoneId:guid}/extend")]
    [Authorize]
    public async Task<ActionResult<MilestoneActionResultVm>> ExtendMilestone(Guid groupId, Guid milestoneId, [FromBody] ExtendMilestoneRequest req, CancellationToken ct)
        => Ok(await service.ExtendMilestoneAsync(groupId, milestoneId, GetUserId(), req, ct));

    [HttpPost("milestones/{milestoneId:guid}/move-tasks")]
    [Authorize]
    public async Task<ActionResult<MilestoneActionResultVm>> MoveMilestoneTasks(Guid groupId, Guid milestoneId, [FromBody] MoveMilestoneTasksRequest req, CancellationToken ct)
        => Ok(await service.MoveMilestoneTasksAsync(groupId, milestoneId, GetUserId(), req, ct));

    // Timeline
    [HttpGet("timeline")]
    [Authorize]
    public Task<TimelineVm> GetTimeline(Guid groupId, [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken ct)
        => service.GetTimelineAsync(groupId, GetUserId(), startDate, endDate, ct);
}
