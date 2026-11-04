namespace Slowcooker;

/// <summary>Per-call overrides for where and when a job runs. Everything is optional; the job type's attributes and defaults apply otherwise.</summary>
public sealed record JobOptions
{
    /// <summary>The queue to place the job on. Null uses the job type's <see cref="QueueAttribute"/>, or <see cref="DefaultQueue"/>.</summary>
    public string? Queue { get; init; }

    /// <summary>When the job becomes eligible to run. Null means immediately (or after <see cref="Delay"/>).</summary>
    public DateTimeOffset? RunAt { get; init; }

    /// <summary>How long after creation the job becomes eligible to run. Ignored when <see cref="RunAt"/> is set.</summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>Overrides the job's own <see cref="IJob.IdempotencyKey"/>.</summary>
    public string? IdempotencyKey { get; init; }

    public const string DefaultQueue = "default";

    public static JobOptions Default { get; } = new();
}
