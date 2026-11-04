namespace Slowcooker.Storage;

/// <summary>Filter and paging for <see cref="IJobStorage.QueryAsync"/>.</summary>
public sealed record JobQuery
{
    public JobState? State { get; init; }
    public string? Queue { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;

    public const int MaxTake = 500;

    public JobQuery Normalized() => this with
    {
        Skip = Math.Max(0, Skip),
        Take = Math.Clamp(Take, 1, MaxTake),
        Queue = string.IsNullOrWhiteSpace(Queue) ? null : Queue,
    };
}

/// <summary>One page of jobs plus the total count matching the filter.</summary>
public sealed record JobPage(IReadOnlyList<StoredJob> Items, long Total);
