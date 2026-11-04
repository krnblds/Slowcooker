namespace Slowcooker.Tests.Support;

public static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null, string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Condition not met within timeout{(because is null ? "" : ": " + because)}");
            await Task.Delay(20);
        }
    }

    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Condition not met within timeout{(because is null ? "" : ": " + because)}");
            await Task.Delay(20);
        }
    }
}
