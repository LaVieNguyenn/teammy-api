using Teammy.Application.Feedback.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IGroupFeedbackRepository
{
    Task<Guid> CreateAsync(GroupFeedbackCreateModel model, CancellationToken ct);
    Task UpdateStatusAsync(Guid feedbackId, string status, Guid? acknowledgedByUserId, string? note, CancellationToken ct);
}
