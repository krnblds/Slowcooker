namespace Slowcooker;

/// <summary>A job together with its scheduling and execution state. This is what storage providers persist.</summary>
public sealed record StoredJob
{
    public required string Id { get; init; }
    public required Job Job { get; init; }
    public required string Queue { get; init; }
    public required JobState State { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the job becomes eligible to run. Moved forward on retry.</summary>
    public required DateTimeOffset RunAt { get; init; }

    /// <summary>
    /// How many times a worker has fetched the job. Incremented when fetched, not when it finishes, so the first run sees 1
    /// and a job that crashes the process still burns an attempt.
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>The most recent exception, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>Id of the worker that holds the lease while <see cref="State"/> is <see cref="JobState.Processing"/>.</summary>
    public string? LeaseOwner { get; init; }

    /// <summary>When the lease expires. After this, another worker may fetch the job (crash recovery).</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>When the job reached <see cref="JobState.Succeeded"/> or <see cref="JobState.Failed"/>.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    public string? IdempotencyKey { get; init; }
}
