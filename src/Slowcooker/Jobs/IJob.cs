namespace Slowcooker;

/// <summary>
/// A unit of background work, described as data. Make it a record:
/// <code>
/// [JobName("send-email")]
/// public sealed record SendEmail(string To, string Subject) : IJob;
/// </code>
/// The record is serialized to JSON when enqueued and handed to the matching <see cref="IJobHandler{TJob}"/> when it runs.
/// Because the job is just data, it survives refactoring of the code that handles it, and the handler gets its
/// dependencies from dependency injection like any other service.
/// </summary>
public interface IJob
{
    /// <summary>
    /// Override to make creating this job idempotent. When a job with the same key already exists, enqueueing returns
    /// the existing job's id instead of creating a duplicate. Keys are freed when the job is deleted or purged.
    /// <code>public string IdempotencyKey => $"charge:{OrderId}";</code>
    /// </summary>
    string? IdempotencyKey => null;
}
