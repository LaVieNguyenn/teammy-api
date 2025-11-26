using Teammy.Application.ProjectTracking.Dtos;

namespace Teammy.Application.ProjectTracking.Interfaces;

public interface IProjectTrackingRepository
{
    Task<Guid> CreateBacklogItemAsync(Guid groupId, Guid createdBy, CreateBacklogItemRequest req, CancellationToken ct);
    Task UpdateBacklogItemAsync(Guid backlogItemId, Guid groupId, UpdateBacklogItemRequest req, CancellationToken ct);
    Task ArchiveBacklogItemAsync(Guid backlogItemId, Guid groupId, CancellationToken ct);

    Task<Guid> CreateMilestoneAsync(Guid groupId, Guid createdBy, CreateMilestoneRequest req, CancellationToken ct);
    Task UpdateMilestoneAsync(Guid milestoneId, Guid groupId, UpdateMilestoneRequest req, CancellationToken ct);
    Task DeleteMilestoneAsync(Guid milestoneId, Guid groupId, CancellationToken ct);

    Task AssignMilestoneItemsAsync(Guid milestoneId, Guid groupId, IReadOnlyList<Guid> backlogItemIds, CancellationToken ct);
    Task RemoveMilestoneItemAsync(Guid milestoneId, Guid backlogItemId, Guid groupId, CancellationToken ct);
}
