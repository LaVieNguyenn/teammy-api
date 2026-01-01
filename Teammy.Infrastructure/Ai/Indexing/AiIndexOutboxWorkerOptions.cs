namespace Teammy.Infrastructure.Ai.Indexing;

public sealed class AiIndexOutboxWorkerOptions
{
    public const string SectionName = "Ai:IndexOutboxWorker";

    public bool Active { get; set; } = true;
}
