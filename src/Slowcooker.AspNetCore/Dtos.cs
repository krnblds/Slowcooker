using System.Text.Json;

namespace Slowcooker.AspNetCore;

/// <summary>Wire shapes of the dashboard API. Property names are camelCase in JSON; enums are camelCase strings.</summary>
public sealed record JobDto(
    string Id,
    string Name,
    string DisplayName,
    JsonElement Payload,
    string Queue,
    JobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset RunAt,
    int Attempt,
    string? LastError,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? CompletedAt,
    string? IdempotencyKey)
{
    public static JobDto From(StoredJob job) => new(
        job.Id, job.Job.Name, job.Job.DisplayName, ParsePayload(job.Job.Payload), job.Queue, job.State,
        job.CreatedAt, job.RunAt, job.Attempt, job.LastError, job.LeaseOwner, job.LeaseExpiresAt, job.CompletedAt, job.IdempotencyKey);

    internal static JsonElement ParsePayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            return doc.RootElement.Clone();
        }
    }
}

public sealed record RecurringJobDto(
    string Id,
    string Name,
    string DisplayName,
    JsonElement Payload,
    string Cron,
    string TimeZoneId,
    string Queue,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastJobId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static RecurringJobDto From(RecurringJob job) => new(
        job.Id, job.Job.Name, job.Job.DisplayName, JobDto.ParsePayload(job.Job.Payload), job.Cron, job.TimeZoneId, job.Queue,
        job.NextRunAt, job.LastRunAt, job.LastJobId, job.CreatedAt, job.UpdatedAt);
}

public sealed record QueueDto(string Name, long Scheduled, long Processing, long Succeeded, long Failed);

public sealed record StatsDto(long Scheduled, long Processing, long Succeeded, long Failed, long Recurring, IReadOnlyList<QueueDto> Queues, DateTimeOffset ServerTime);

public sealed record PageDto<T>(IReadOnlyList<T> Items, long Total, int Skip, int Take);

public sealed record InfoDto(string Version, string Storage, DateTimeOffset ServerTime);

public sealed record TriggeredDto(string JobId);

public sealed record ErrorDto(string Error);

public sealed record CronPreviewDto(string Expression, bool Valid, string? Error, string TimeZoneId, IReadOnlyList<DateTimeOffset> Upcoming);
