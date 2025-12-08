namespace Teammy.Application.Activity.Dtos;

public sealed record ActivityLogRecord(
    Guid ActorId,
    string EntityType,
    string Action,
    Guid? GroupId,
    Guid? EntityId,
    Guid? TargetUserId,
    string? Message,
    string? MetadataJson,
    string Status,
    string? Platform,
    string Severity);
