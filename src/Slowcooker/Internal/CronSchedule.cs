namespace Slowcooker.Internal;

/// <summary>Internal shim kept for the scheduler and client; <see cref="Cron"/> is the public API.</summary>
internal readonly struct CronSchedule(Cron cron)
{
    public static CronSchedule Parse(string expression) => new(Cron.Parse(expression));

    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone) => cron.Next(after, timeZone);
}
