using Slowcooker;

namespace Slowcooker.Samples.WebApi.Jobs;

/// <summary>A job is a record. The name is what gets stored, so the record can be renamed later without breaking queued jobs.</summary>
[JobName("send-welcome-email")]
[Queue("email")]
public sealed record SendWelcomeEmail(string To, string Name) : IJob
{
    // Enqueueing the same welcome twice (double-submitted form, retried HTTP call) creates one job, not two.
    public string IdempotencyKey => $"welcome:{To.ToLowerInvariant()}";
}

public sealed class SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger) : IJobHandler<SendWelcomeEmail>
{
    public async Task HandleAsync(SendWelcomeEmail job, JobContext context)
    {
        logger.LogInformation("Sending welcome email to {To} (attempt {Attempt})", job.To, context.Attempt);
        await Task.Delay(Random.Shared.Next(200, 1500), context.CancellationToken); // pretend to talk to an email provider

        // Addresses at flaky.example time out twice and then succeed; dead.example never succeeds (ends up Failed).
        if (job.To.EndsWith("@flaky.example", StringComparison.OrdinalIgnoreCase) && context.Attempt < 3)
            throw new TimeoutException("The email provider timed out. It will be retried with backoff.");
        if (job.To.EndsWith("@dead.example", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Mailbox {job.To} does not exist (550 5.1.1).");

        logger.LogInformation("Welcome email delivered to {To}", job.To);
    }
}
