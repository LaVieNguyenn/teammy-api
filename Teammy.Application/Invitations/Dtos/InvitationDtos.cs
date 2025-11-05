namespace Teammy.Application.Invitations.Dtos;

public sealed record InvitationListItemDto(
    Guid   InvitationId,
    Guid   PostId,
    string Status,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    Guid   InvitedBy,
    string? InvitedByName,
    Guid?  GroupId,
    string? GroupName
);

public sealed record InvitationDetailDto(
    Guid   InvitationId,
    Guid   PostId,
    Guid   InviteeUserId,
    Guid   InvitedBy,
    string Status,
    DateTime CreatedAt,
    DateTime? RespondedAt,
    DateTime? ExpiresAt,
    Guid?  GroupId,
    Guid   SemesterId,
    string? GroupName,
    string? InviteeEmail
);

