namespace Teammy.Application.Activity.Dtos;

public sealed record ActivityLogCreateRequest(
    Guid ActorId,
    string EntityType,
    string Action)
{
    public Guid? GroupId { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? TargetUserId { get; init; }
    public string? Message { get; init; }
    public object? Metadata { get; init; }
    public string Status { get; init; } = "success";
    public string? Platform { get; init; }
    public string Severity { get; init; } = "info";
}
