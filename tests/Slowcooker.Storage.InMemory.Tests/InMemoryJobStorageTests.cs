using Slowcooker.Storage;
using Slowcooker.Storage.ContractTests;
using Slowcooker.Storage.InMemory;

namespace Slowcooker.Storage.InMemory.Tests;

public sealed class InMemoryJobStorageTests : JobStorageContractTests
{
    protected override Task<IJobStorage> CreateStorageAsync(TimeProvider timeProvider)
        => Task.FromResult<IJobStorage>(new InMemoryJobStorage(timeProvider));
}
