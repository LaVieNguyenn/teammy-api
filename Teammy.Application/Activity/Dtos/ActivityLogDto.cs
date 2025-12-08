namespace Teammy.Application.Activity.Dtos;

public sealed record ActivityLogDto(
    Guid ActivityId,
    Guid? GroupId,
    string EntityType,
    Guid? EntityId,
    string Action,
    Guid ActorId,
    string? ActorDisplayName,
    string? ActorEmail,
    Guid? TargetUserId,
    string? TargetDisplayName,
    string? Message,
    string? Metadata,
    string Status,
    string? Platform,
    string Severity,
    DateTime CreatedAt);
