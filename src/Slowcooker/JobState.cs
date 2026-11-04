namespace Slowcooker;

/// <summary>The lifecycle of a job. There are deliberately only four states.</summary>
public enum JobState
{
    /// <summary>Waiting to run. Becomes eligible once <see cref="StoredJob.RunAt"/> has passed.</summary>
    Scheduled = 0,

    /// <summary>Claimed by a worker and currently executing. Protected by a lease that expires at <see cref="StoredJob.LeaseExpiresAt"/>.</summary>
    Processing = 1,

    /// <summary>Finished without throwing.</summary>
    Succeeded = 2,

    /// <summary>Threw and the retry policy gave up.</summary>
    Failed = 3,
}
