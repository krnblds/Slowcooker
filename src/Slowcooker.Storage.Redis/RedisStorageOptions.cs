namespace Slowcooker.Storage.Redis;

public sealed class RedisStorageOptions
{
    /// <summary>
    /// Prefix for every key. Default "slowcooker". On Redis Cluster wrap it in braces, e.g. "{slowcooker}", so all keys
    /// land in one hash slot; the provider's scripts touch several keys per call.
    /// </summary>
    public string KeyPrefix { get; set; } = "slowcooker";

    /// <summary>Logical database. -1 (default) uses the connection's default.</summary>
    public int Database { get; set; } = -1;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(KeyPrefix) || KeyPrefix.Contains(':'))
            throw new InvalidOperationException("RedisStorageOptions.KeyPrefix must be non-empty and must not contain ':'.");
    }
}
