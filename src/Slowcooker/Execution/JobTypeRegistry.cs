using System.Collections.Concurrent;
using System.Reflection;
using Slowcooker.Internal;

namespace Slowcooker.Execution;

/// <summary>A job type registered through <c>AddHandler</c>; the registry is built from these.</summary>
public sealed record JobTypeRegistration(Type JobType);

/// <summary>
/// Maps stable job names to CLR types and back. Names come from <see cref="JobNameAttribute"/> when present, otherwise
/// from the type's version-free CLR name. Types registered through handlers resolve by name; anything else falls back to
/// <see cref="Type.GetType(string)"/> so unregistered-but-loadable types still work.
/// </summary>
public sealed class JobTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _byType = new();

    public JobTypeRegistry() : this([]) { }

    public JobTypeRegistry(IEnumerable<JobTypeRegistration> registrations)
    {
        foreach (var registration in registrations) Register(registration.JobType);
    }

    public void Register(Type jobType)
    {
        ArgumentNullException.ThrowIfNull(jobType);
        if (!typeof(IJob).IsAssignableFrom(jobType))
            throw new ArgumentException($"{jobType} does not implement {nameof(IJob)}.", nameof(jobType));

        var name = NameOf(jobType);
        var existing = _byName.GetOrAdd(name, jobType);
        if (existing != jobType)
            throw new InvalidOperationException($"Job name '{name}' is used by both {existing} and {jobType}. Give one of them a different [JobName].");
        _byType[jobType] = name;
    }

    public string GetName(Type jobType) => _byType.GetOrAdd(jobType, NameOf);

    public Type Resolve(string name)
    {
        if (_byName.TryGetValue(name, out var type)) return type;

        var loaded = Type.GetType(name, throwOnError: false);
        if (loaded is null || !typeof(IJob).IsAssignableFrom(loaded))
            throw new JobExecutionException($"No job type is registered under the name '{name}'. Register its handler with AddHandler<...>() in the worker process, or check that the assembly is referenced.");

        _byName.TryAdd(name, loaded);
        return loaded;
    }

    public IReadOnlyCollection<Type> RegisteredTypes => _byType.Keys.ToArray();

    private static string NameOf(Type jobType)
        => jobType.GetCustomAttribute<JobNameAttribute>()?.Name ?? TypeNames.Serialize(jobType);
}
