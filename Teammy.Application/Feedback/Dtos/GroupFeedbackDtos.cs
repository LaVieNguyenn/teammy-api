using System.ComponentModel.DataAnnotations;

namespace Teammy.Application.Feedback.Dtos;

public sealed class SubmitGroupFeedbackRequest
{
    public string? Category { get; init; }

    [Required]
    public string Summary { get; init; } = string.Empty;

    public string? Details { get; init; }
    public int? Rating { get; init; }
    public string? Blockers { get; init; }
    public string? NextSteps { get; init; }
}

public sealed record GroupFeedbackDto(
    Guid FeedbackId,
    Guid GroupId,
    Guid MentorId,
    string MentorName,
    string? MentorEmail,
    string? MentorAvatar,
    string? Category,
    string Summary,
    string? Details,
    int? Rating,
    string? Blockers,
    string? NextSteps,
    string Status,
    string? AcknowledgedNote,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? AcknowledgedAt);

public sealed class UpdateGroupFeedbackStatusRequest
{
    [Required]
    public string Status { get; init; } = string.Empty;

    public string? Note { get; init; }
}

public sealed record GroupFeedbackCreateModel(
    Guid GroupId,
    Guid SemesterId,
    Guid MentorId,
    string? Category,
    string Summary,
    string? Details,
    int? Rating,
    string? Blockers,
    string? NextSteps);
