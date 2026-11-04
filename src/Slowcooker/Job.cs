namespace Slowcooker;

/// <summary>
/// The stored form of a job: the stable name of its type and its JSON payload. You normally never build one of these;
/// <see cref="IJobClient.EnqueueAsync{TJob}"/> serializes your <see cref="IJob"/> record into it.
/// </summary>
/// <param name="Name">The job's stable name: the value of <see cref="JobNameAttribute"/>, or "Namespace.Type, Assembly" when the type has none.</param>
/// <param name="Payload">The job record serialized as JSON.</param>
public sealed record Job(string Name, string Payload)
{
    /// <summary>A short, human-friendly name such as <c>SendEmail</c>.</summary>
    public string DisplayName
    {
        get
        {
            var name = Name;
            var comma = name.IndexOf(',');
            if (comma >= 0) name = name[..comma];
            var dot = name.LastIndexOf('.');
            return dot >= 0 ? name[(dot + 1)..] : name;
        }
    }
}
