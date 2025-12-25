namespace Teammy.Application.Common.Interfaces;

public interface IGroupStatusNotifier
{
    Task NotifyGroupStatusAsync(Guid groupId, Guid userId, string status, string action, CancellationToken ct);
}
