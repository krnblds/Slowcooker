namespace Slowcooker.Storage;

/// <summary>
/// Thrown by <see cref="IFetchedJob"/> when the worker's lease on a job has been lost: the lease expired and another
/// worker fetched the job, or the job was deleted. The storage is left untouched; the job belongs to someone else now.
/// </summary>
public sealed class LeaseLostException(string jobId)
    : Exception($"The lease on job '{jobId}' was lost; it has expired and may have been fetched by another worker, or the job was deleted.")
{
    public string JobId { get; } = jobId;
}
