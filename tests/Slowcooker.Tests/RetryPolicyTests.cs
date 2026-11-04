namespace Slowcooker.Tests;

public class RetryPolicyTests
{
    [Fact]
    public void Default_policy_retries_up_to_ten_attempts_with_growing_delays()
    {
        var policy = new ExponentialBackoffRetryPolicy();
        var ex = new InvalidOperationException();

        var delays = Enumerable.Range(1, 9).Select(a => policy.GetRetryDelay(a, ex)).ToList();

        Assert.All(delays, d => Assert.NotNull(d));
        Assert.InRange(delays[0]!.Value, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));
        Assert.InRange(delays[8]!.Value, TimeSpan.FromSeconds(4096 + 15), TimeSpan.FromSeconds(4096 + 15 + 30 * 9));
        Assert.Null(policy.GetRetryDelay(10, ex));
        Assert.Null(policy.GetRetryDelay(11, ex));
    }

    [Fact]
    public void Delays_are_capped()
    {
        var policy = new ExponentialBackoffRetryPolicy { MaxAttempts = 100, MaxDelay = TimeSpan.FromMinutes(1) };
        Assert.Equal(TimeSpan.FromMinutes(1), policy.GetRetryDelay(50, new Exception()));
    }

    [Fact]
    public void Job_load_failures_are_retried_so_rolling_deploys_are_safe()
        => Assert.NotNull(new ExponentialBackoffRetryPolicy().GetRetryDelay(1, new JobExecutionException("unknown job type")));

    [Fact]
    public void Configured_exception_types_are_not_retried_including_subtypes()
    {
        var policy = new ExponentialBackoffRetryPolicy { DoNotRetry = [typeof(ArgumentException)] };
        Assert.Null(policy.GetRetryDelay(1, new ArgumentNullException()));
        Assert.NotNull(policy.GetRetryDelay(1, new InvalidOperationException()));
    }

    [Fact]
    public void NoRetryPolicy_gives_up_immediately()
        => Assert.Null(NoRetryPolicy.Instance.GetRetryDelay(1, new Exception()));
}
