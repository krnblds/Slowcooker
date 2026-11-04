using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Slowcooker.Execution;
using Slowcooker.Storage;
using Slowcooker.Storage.InMemory;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

public class ServiceCollectionTests
{
    [Fact]
    public void Resolving_the_client_without_a_storage_gives_a_helpful_error()
    {
        var services = new ServiceCollection();
        services.AddSlowcooker();
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IJobClient>());
        Assert.Contains("UseInMemoryStorage", ex.Message);
    }

    [Fact]
    public void Registers_client_monitor_and_recurring_client()
    {
        var services = new ServiceCollection();
        services.AddSlowcooker().UseInMemoryStorage();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryJobStorage>(provider.GetRequiredService<IJobStorage>());
        Assert.NotNull(provider.GetRequiredService<IJobClient>());
        Assert.NotNull(provider.GetRequiredService<IRecurringJobClient>());
        Assert.NotNull(provider.GetRequiredService<IJobMonitor>());
        Assert.IsType<ExponentialBackoffRetryPolicy>(provider.GetRequiredService<IRetryPolicy>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void AddHandler_registers_every_handled_job_type_by_name()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddSlowcooker().UseInMemoryStorage().AddHandler<TestHandlers>();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<JobTypeRegistry>();
        Assert.Equal(typeof(Note), registry.Resolve("record"));
        Assert.Equal(typeof(ChargeCard), registry.Resolve("charge-card"));
        Assert.Contains(typeof(Unnamed), registry.RegisteredTypes);

        using var scope = provider.CreateScope();
        Assert.IsType<TestHandlers>(scope.ServiceProvider.GetRequiredService<IJobHandler<Note>>());
    }

    [Fact]
    public void AddHandlersFrom_scans_assemblies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddSlowcooker().UseInMemoryStorage().AddHandlersFrom(typeof(TestHandlers).Assembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<TestHandlers>(scope.ServiceProvider.GetRequiredService<IJobHandler<WaitForGate>>());
    }

    [Fact]
    public void AddHandler_rejects_types_that_handle_nothing()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddSlowcooker().AddHandler<Recorder>());
    }

    [Fact]
    public void AddWorker_registers_the_hosted_worker_with_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSlowcooker().UseInMemoryStorage().AddWorker(o => o.Queues = ["a", "b"]);
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IHostedService>());
        Assert.Equal(["a", "b"], provider.GetRequiredService<IOptions<WorkerOptions>>().Value.Queues);
    }

    [Fact]
    public async Task A_registered_TimeProvider_is_used_by_storage_and_client()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSlowcooker().UseInMemoryStorage();
        await using var provider = services.BuildServiceProvider();

        var id = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new Note("x"));
        var job = await provider.GetRequiredService<IJobStorage>().GetAsync(id, CancellationToken.None);
        Assert.Equal(time.GetUtcNow(), job!.CreatedAt);
    }
}
