namespace Teammy.Application.Groups.Dtos;

public sealed record GroupPendingItemDto(
    string Type,            // join_request | application | invitation
    Guid   Id,              // group_member_id | candidate_id | invitation_id
    Guid?  PostId,          // for application/invitation
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    DateTime CreatedAt,
    string? Message
);

