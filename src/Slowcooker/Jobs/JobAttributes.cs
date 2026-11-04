namespace Slowcooker;

/// <summary>
/// Gives a job type a stable name that is stored instead of its CLR type name. With it you can rename or move the
/// record without breaking jobs that are already queued. Strongly recommended for anything that reaches production.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class JobNameAttribute(string name) : Attribute
{
    public string Name { get; } = !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentException("Job name must not be empty.", nameof(name));
}

/// <summary>The queue jobs of this type go to unless <see cref="JobOptions.Queue"/> says otherwise.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class QueueAttribute(string queue) : Attribute
{
    public string Queue { get; } = !string.IsNullOrWhiteSpace(queue) ? queue : throw new ArgumentException("Queue name must not be empty.", nameof(queue));
}
