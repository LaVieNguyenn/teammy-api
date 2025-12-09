namespace Teammy.Application.Activity.Dtos;

public sealed class ActivityLogListRequest
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public int Limit { get; init; } = DefaultLimit;
    public DateTime? Before { get; init; }
    public string? EntityType { get; init; }
    public string? Action { get; init; }
    public Guid? GroupId { get; init; }

    public int GetEffectiveLimit()
    {
        if (Limit <= 0) return DefaultLimit;
        return Limit > MaxLimit ? MaxLimit : Limit;
    }
}
