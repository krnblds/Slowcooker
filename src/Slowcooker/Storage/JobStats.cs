namespace Slowcooker.Storage;

/// <summary>Counts by state, overall and per queue.</summary>
public sealed record JobStats(
    long Scheduled,
    long Processing,
    long Succeeded,
    long Failed,
    long Recurring,
    IReadOnlyList<QueueStats> Queues)
{
    public static JobStats Empty { get; } = new(0, 0, 0, 0, 0, []);
}

public sealed record QueueStats(string Name, long Scheduled, long Processing, long Succeeded, long Failed);
