namespace Slowcooker;

/// <summary>A job template that is triggered on a cron schedule.</summary>
public sealed record RecurringJob
{
    public required string Id { get; init; }
    public required Job Job { get; init; }

    /// <summary>A 5-field cron expression (minute precision) or a 6-field one (seconds precision).</summary>
    public required string Cron { get; init; }

    /// <summary>IANA or Windows time zone id used to interpret <see cref="Cron"/>. Default UTC.</summary>
    public string TimeZoneId { get; init; } = "UTC";

    public string Queue { get; init; } = JobOptions.DefaultQueue;

    /// <summary>When the scheduler will next trigger this job. Null when the cron expression has no future occurrence.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>Id of the most recently created job for this schedule.</summary>
    public string? LastJobId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Options for <see cref="IRecurringJobClient.AddOrUpdateAsync"/>.</summary>
public sealed record RecurringJobOptions
{
    /// <summary>The queue created jobs go to. Null uses the job type's <see cref="QueueAttribute"/>, or the default queue.</summary>
    public string? Queue { get; init; }

    /// <summary>Time zone the cron expression is evaluated in. Default UTC. Daylight-saving transitions are handled by Cronos.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    public static RecurringJobOptions Default { get; } = new();
}
