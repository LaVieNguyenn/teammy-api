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
    public bool RequiresAdminAttention { get; init; }
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
    bool RequiresAdminAttention,
    string Status,
    string? AcknowledgedNote,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? AcknowledgedAt);

public sealed record UpdateGroupFeedbackStatusRequest(
    [property: Required] string Status,
    string? Note);

public sealed record GroupFeedbackCreateModel(
    Guid GroupId,
    Guid SemesterId,
    Guid MentorId,
    string? Category,
    string Summary,
    string? Details,
    int? Rating,
    string? Blockers,
    string? NextSteps,
    bool RequiresAdminAttention);
