namespace Teammy.Application.Kanban.Interfaces;

public interface IGroupAccessQueries
{
    Task<bool> IsMemberAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<bool> IsLeaderAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<bool> IsMentorAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<bool> IsGroupActiveAsync(Guid groupId, CancellationToken ct);
}
