namespace Slowcooker;

/// <summary>Never retries: a job that throws is marked <see cref="JobState.Failed"/> immediately.</summary>
public sealed class NoRetryPolicy : IRetryPolicy
{
    public static NoRetryPolicy Instance { get; } = new();

    public TimeSpan? GetRetryDelay(int attempt, Exception exception) => null;
}
