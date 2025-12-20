namespace Teammy.Application.Groups.Dtos;

public sealed record GroupPendingItemDto(
    string Type,            
    Guid   Id,             
    Guid?  PostId,         
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    DateTime CreatedAt,
    string? Message,
    Guid? TopicId,
    string? TopicTitle,
    DateTime? RespondedAt
);
