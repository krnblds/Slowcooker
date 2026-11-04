namespace Slowcooker;

/// <summary>
/// The default policy. Retries with a growing, jittered delay and gives up after <see cref="MaxAttempts"/>.
/// The curve matches what Hangfire has converged on after years of production use:
/// <c>(attempt - 1)^4 + 15 + random(0..30) * attempt</c> seconds, capped at <see cref="MaxDelay"/>.
/// With 10 attempts the final retry is scheduled roughly 2 hours after the first failure.
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    /// <summary>Total attempts (first run plus retries). Default 10, like Hangfire.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>Upper bound for a single delay. Default 1 day.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Exceptions that should never be retried (subtypes match too). Note that <see cref="JobExecutionException"/>, thrown when a job
    /// type or handler cannot be resolved, is retried on purpose: during a rolling deploy an old worker may fetch a job type it does
    /// not know yet, and the next attempt lands on a new worker. Add it here if you prefer such jobs to fail immediately.
    /// </summary>
    public IReadOnlyCollection<Type> DoNotRetry { get; init; } = [];

    public TimeSpan? GetRetryDelay(int attempt, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (attempt >= MaxAttempts) return null;
        foreach (var type in DoNotRetry)
            if (type.IsInstanceOfType(exception)) return null;

        var seconds = Math.Pow(attempt - 1, 4) + 15 + Random.Shared.Next(30) * attempt;
        var delay = TimeSpan.FromSeconds(Math.Round(seconds));
        return delay > MaxDelay ? MaxDelay : delay;
    }
}
