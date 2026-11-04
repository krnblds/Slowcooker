using Slowcooker;

namespace Slowcooker.Samples.WebApi.Jobs;

[JobName("generate-daily-report")]
public sealed record GenerateDailyReport(string Region) : IJob;

public sealed class GenerateDailyReportHandler(ILogger<GenerateDailyReportHandler> logger) : IJobHandler<GenerateDailyReport>
{
    public async Task HandleAsync(GenerateDailyReport job, JobContext context)
    {
        logger.LogInformation("Generating the daily report for {Region}", job.Region);
        await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);

        // Handlers can chain work: fan out a follow-up job per recipient.
        await context.Jobs.EnqueueAsync(new SendWelcomeEmail($"ops-{job.Region}@example.com", "Ops"), cancellationToken: context.CancellationToken);
    }
}
