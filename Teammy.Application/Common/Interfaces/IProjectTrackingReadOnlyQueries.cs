using Teammy.Application.ProjectTracking.Dtos;

namespace Teammy.Application.ProjectTracking.Interfaces;

public interface IProjectTrackingReadOnlyQueries
{
    Task<IReadOnlyList<BacklogItemVm>> ListBacklogAsync(Guid groupId, CancellationToken ct);
    Task<BacklogItemVm?> GetBacklogItemAsync(Guid backlogItemId, Guid groupId, CancellationToken ct);
    Task<IReadOnlyList<MilestoneVm>> ListMilestonesAsync(Guid groupId, CancellationToken ct);
    Task<MilestoneVm?> GetMilestoneAsync(Guid milestoneId, CancellationToken ct);
    Task<ProjectReportVm> BuildProjectReportAsync(Guid groupId, Guid? milestoneId, CancellationToken ct);
}
