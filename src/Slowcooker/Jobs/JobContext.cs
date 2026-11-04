namespace Slowcooker;

/// <summary>Everything a handler may want to know about the job it is running, plus a client to enqueue follow-up jobs.</summary>
public sealed class JobContext
{
    internal JobContext(StoredJob stored, IJobClient jobs, CancellationToken cancellationToken)
    {
        Id = stored.Id;
        Attempt = stored.Attempt;
        Queue = stored.Queue;
        CreatedAt = stored.CreatedAt;
        RunAt = stored.RunAt;
        LastError = stored.LastError;
        Jobs = jobs;
        CancellationToken = cancellationToken;
    }

    public string Id { get; }

    /// <summary>1 on the first run, 2 on the first retry, and so on.</summary>
    public int Attempt { get; }

    public string Queue { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset RunAt { get; }

    /// <summary>The exception from the previous attempt, if this is a retry.</summary>
    public string? LastError { get; }

    /// <summary>Enqueue follow-up jobs from inside a handler.</summary>
    public IJobClient Jobs { get; }

    /// <summary>
    /// Cancelled when the worker shuts down or loses its lease on this job. Pass it to everything that accepts one:
    /// a cancelled job is requeued and runs again elsewhere, so stopping early is the right thing to do.
    /// </summary>
    public CancellationToken CancellationToken { get; }
}
