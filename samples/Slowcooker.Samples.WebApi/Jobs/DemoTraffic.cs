using Slowcooker;

namespace Slowcooker.Samples.WebApi.Jobs;

/// <summary>
/// Keeps the dashboard interesting: enqueues a steady trickle of jobs, some of which fail and retry.
/// Enabled with <c>Slowcooker:DemoTraffic=true</c> (on by default in Development).
/// </summary>
public sealed class DemoTraffic(IJobClient jobs, ILogger<DemoTraffic> logger) : BackgroundService
{
    private static readonly string[] Names = ["Ada", "Grace", "Linus", "Margaret", "Alan", "Barbara", "Dennis", "Ken"];
    private static readonly string[] Domains = ["example.com", "example.com", "example.com", "example.org", "flaky.example", "dead.example"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Demo traffic generator running; disable with Slowcooker:DemoTraffic=false");
        var n = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                n++;
                var name = Names[Random.Shared.Next(Names.Length)];
                var domain = Domains[Random.Shared.Next(Domains.Length)];
                var email = $"{name.ToLowerInvariant()}{n}@{domain}";

                if (n % 7 == 0)
                    await jobs.ScheduleAsync(new SendWelcomeEmail(email, name), TimeSpan.FromSeconds(Random.Shared.Next(30, 120)), cancellationToken: stoppingToken);
                else
                    await jobs.EnqueueAsync(new SendWelcomeEmail(email, name), cancellationToken: stoppingToken);

                if (n % 15 == 0)
                    await jobs.EnqueueAsync(new GenerateDailyReport(n % 2 == 0 ? "eu" : "us"), cancellationToken: stoppingToken);

                await Task.Delay(Random.Shared.Next(1500, 4000), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Demo traffic failed to enqueue; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

/// <summary>Retries quickly so retries are visible within a demo session instead of hours later.</summary>
public sealed class DemoRetryPolicy : IRetryPolicy
{
    public TimeSpan? GetRetryDelay(int attempt, Exception exception)
        => attempt >= 4 ? null : TimeSpan.FromSeconds(10 * attempt);
}
