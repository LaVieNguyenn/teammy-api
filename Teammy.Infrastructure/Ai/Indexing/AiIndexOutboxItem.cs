using System;

namespace Teammy.Infrastructure.Ai.Indexing;

public enum AiIndexAction
{
    Upsert = 1,
    Delete = 2
}

public sealed class AiIndexOutboxItem
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string Type { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string PointId { get; set; } = string.Empty;
    public AiIndexAction Action { get; set; }

    public Guid SemesterId { get; set; }
    public Guid? MajorId { get; set; }

    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}
