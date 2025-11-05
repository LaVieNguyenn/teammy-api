using Teammy.Application.Groups.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IGroupRepository
{
    Task<Guid> CreateGroupAsync(Guid semesterId, Guid? topicId, Guid? majorId,
        string name, string? description, int maxMembers, CancellationToken ct);

    Task AddMembershipAsync(Guid groupId, Guid userId, Guid semesterId, string status, CancellationToken ct);

    Task UpdateMembershipStatusAsync(Guid groupMemberId, string newStatus, CancellationToken ct);

    Task DeleteMembershipAsync(Guid groupMemberId, CancellationToken ct);

    Task<bool> LeaveGroupAsync(Guid groupId, Guid userId, CancellationToken ct);
}
