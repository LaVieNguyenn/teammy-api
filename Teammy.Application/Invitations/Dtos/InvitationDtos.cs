namespace Teammy.Application.Invitations.Dtos;

public sealed record InvitationListItemDto(
    Guid   InvitationId,
    string Type,
    string Status,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    Guid   InvitedBy,
    string? InvitedByName,
    Guid   GroupId,
    string? GroupName,
    Guid?  TopicId,
    string? TopicTitle
);

public sealed record InvitationDetailDto(
    Guid   InvitationId,
    Guid   InviteeUserId,
    Guid   InvitedBy,
    string Type,
    string Status,
    DateTime CreatedAt,
    DateTime? RespondedAt,
    DateTime? ExpiresAt,
    Guid   GroupId,
    Guid   SemesterId,
    string? GroupName,
    string? InviteeEmail,
    Guid?  TopicId,
    string? TopicTitle
);
