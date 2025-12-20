using Teammy.Application.Feedback.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IGroupFeedbackReadOnlyQueries
{
    Task<IReadOnlyList<GroupFeedbackDto>> ListForGroupAsync(Guid groupId, CancellationToken ct);
    Task<GroupFeedbackDto?> GetAsync(Guid feedbackId, CancellationToken ct);
}
