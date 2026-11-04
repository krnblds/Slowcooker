using Microsoft.Extensions.DependencyInjection;
using Slowcooker.Storage.InMemory;

namespace Slowcooker;

public static class InMemoryStorageExtensions
{
    /// <summary>Keeps jobs in process memory. Nothing survives a restart; use it for tests and development.</summary>
    public static SlowcookerBuilder UseInMemoryStorage(this SlowcookerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseStorage(sp => new InMemoryJobStorage(sp.GetService<TimeProvider>()));
    }
}
