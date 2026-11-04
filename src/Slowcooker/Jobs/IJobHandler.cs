namespace Slowcooker;

/// <summary>
/// Runs jobs of one type. Register it with <c>AddSlowcooker().AddHandler&lt;SendEmailHandler&gt;()</c>; it is resolved
/// from a fresh dependency injection scope for every job, so take whatever you need in the constructor.
/// <code>
/// public sealed class SendEmailHandler(IMailer mailer) : IJobHandler&lt;SendEmail&gt;
/// {
///     public Task HandleAsync(SendEmail job, JobContext context)
///         => mailer.SendAsync(job.To, job.Subject, context.CancellationToken);
/// }
/// </code>
/// Throwing marks the attempt failed; the <see cref="IRetryPolicy"/> decides whether it runs again.
/// Jobs are delivered at least once, so handlers must tolerate running twice.
/// </summary>
public interface IJobHandler<in TJob> where TJob : IJob
{
    Task HandleAsync(TJob job, JobContext context);
}
