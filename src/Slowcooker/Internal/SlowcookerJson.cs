using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slowcooker.Internal;

internal static class SlowcookerJson
{
    /// <summary>Serializer settings for job arguments. Compact, camelCase-insensitive, tolerant of enums as strings.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
