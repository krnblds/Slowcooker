namespace Slowcooker;

/// <summary>Decides what happens when a job throws.</summary>
public interface IRetryPolicy
{
    /// <param name="attempt">How many times the job has been claimed so far, including the attempt that just failed (1-based).</param>
    /// <param name="exception">The exception the job threw.</param>
    /// <returns>How long to wait before the next attempt, or null to give up and mark the job <see cref="JobState.Failed"/>.</returns>
    TimeSpan? GetRetryDelay(int attempt, Exception exception);
}
