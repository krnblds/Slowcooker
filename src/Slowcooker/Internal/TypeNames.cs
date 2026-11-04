using System.Collections.Concurrent;

namespace Slowcooker.Internal;

/// <summary>Stable, version-independent type names: "Namespace.Type, Assembly".</summary>
internal static class TypeNames
{
    private static readonly ConcurrentDictionary<Type, string> SerializeCache = new();
    private static readonly ConcurrentDictionary<string, Type> ResolveCache = new();

    public static string Serialize(Type type)
        => SerializeCache.GetOrAdd(type, static t =>
        {
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                // FullName of a closed generic embeds fully-qualified (versioned) argument names. Rebuild it from parts.
                var definition = t.GetGenericTypeDefinition();
                var args = string.Join(",", t.GetGenericArguments().Select(a => $"[{Serialize(a)}]"));
                return $"{definition.FullName}[{args}], {definition.Assembly.GetName().Name}";
            }

            if (t.IsArray)
            {
                var element = t.GetElementType()!;
                var rank = t.GetArrayRank() == 1 ? "[]" : $"[{new string(',', t.GetArrayRank() - 1)}]";
                var elementName = Serialize(element);
                var comma = elementName.LastIndexOf(", ", StringComparison.Ordinal);
                return $"{elementName[..comma]}{rank}, {elementName[(comma + 2)..]}";
            }

            return $"{t.FullName}, {t.Assembly.GetName().Name}";
        });

    public static Type Resolve(string name)
        => ResolveCache.GetOrAdd(name, static n =>
            Type.GetType(n, throwOnError: false)
            ?? throw new JobExecutionException($"Type '{n}' could not be loaded. Is the assembly referenced by the worker?"));
}
