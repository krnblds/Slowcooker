namespace Slowcooker.Storage;

/// <summary>
/// Helper for storage providers: tracks whether the job has been settled, turns a lost lease into
/// <see cref="LeaseLostException"/>, and implements the "requeue on dispose if nobody settled it" rule of <see cref="IFetchedJob"/>.
/// Providers implement the <c>*CoreAsync</c> methods and return false when the lease is no longer theirs.
/// </summary>
public abstract class FetchedJobBase(StoredJob record) : IFetchedJob
{
    private int _settled;

    public StoredJob Record { get; } = record;

    public Task CompleteAsync(CancellationToken cancellationToken = default)
        => SettleAsync(CompleteCoreAsync, cancellationToken);

    public Task FailAsync(string error, CancellationToken cancellationToken = default)
        => SettleAsync(ct => FailCoreAsync(error ?? string.Empty, ct), cancellationToken);

    public Task RetryAsync(DateTimeOffset runAt, string error, CancellationToken cancellationToken = default)
        => SettleAsync(ct => RetryCoreAsync(runAt, error ?? string.Empty, ct), cancellationToken);

    public Task RequeueAsync(CancellationToken cancellationToken = default)
        => SettleAsync(RequeueCoreAsync, cancellationToken);

    public async Task ExtendLeaseAsync(TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _settled) == 1)
            throw new InvalidOperationException($"Job '{Record.Id}' has already been settled.");
        if (!await ExtendLeaseCoreAsync(leaseDuration, cancellationToken).ConfigureAwait(false))
            throw new LeaseLostException(Record.Id);
    }

    public async ValueTask DisposeAsync()
    {
        if (MarkSettled())
        {
            try { await RequeueCoreAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* best effort: the lease will expire on its own */ }
        }
        GC.SuppressFinalize(this);
    }

    private async Task SettleAsync(Func<CancellationToken, Task<bool>> core, CancellationToken cancellationToken)
    {
        if (!MarkSettled())
            throw new InvalidOperationException($"Job '{Record.Id}' has already been settled. Call exactly one of Complete, Fail, Retry or Requeue.");
        if (!await core(cancellationToken).ConfigureAwait(false))
            throw new LeaseLostException(Record.Id);
    }

    /// <summary>True the first time it is called, false afterwards.</summary>
    private bool MarkSettled() => Interlocked.Exchange(ref _settled, 1) == 0;

    protected abstract Task<bool> CompleteCoreAsync(CancellationToken cancellationToken);
    protected abstract Task<bool> FailCoreAsync(string error, CancellationToken cancellationToken);
    protected abstract Task<bool> RetryCoreAsync(DateTimeOffset runAt, string error, CancellationToken cancellationToken);
    protected abstract Task<bool> RequeueCoreAsync(CancellationToken cancellationToken);
    protected abstract Task<bool> ExtendLeaseCoreAsync(TimeSpan leaseDuration, CancellationToken cancellationToken);
}
