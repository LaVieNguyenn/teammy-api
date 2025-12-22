using Teammy.Application.Feedback.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IGroupFeedbackReadOnlyQueries
{
    Task<IReadOnlyList<GroupFeedbackDto>> ListForGroupAsync(Guid groupId, string? status, int skip, int take, CancellationToken ct);
    Task<int> CountForGroupAsync(Guid groupId, string? status, CancellationToken ct);
    Task<GroupFeedbackDto?> GetAsync(Guid feedbackId, CancellationToken ct);
}
