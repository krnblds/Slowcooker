using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Slowcooker.Execution;
using Slowcooker.Storage.InMemory;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

public class JobExecutorTests
{
    private readonly ServiceProvider _services;
    private readonly Recorder _recorder = new();
    private readonly JobExecutor _executor;
    private readonly JobSerializer _serializer;
    private readonly IJobClient _client;
    private readonly InMemoryJobStorage _storage = new(new FakeTimeProvider());

    public JobExecutorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_recorder);
        services.AddSlowcooker().UseStorage(_ => _storage).AddHandler<TestHandlers>().AddJob<NoHandler>();
        _services = services.BuildServiceProvider();
        _executor = _services.GetRequiredService<JobExecutor>();
        _serializer = _services.GetRequiredService<JobSerializer>();
        _client = _services.GetRequiredService<IJobClient>();
    }

    private StoredJob Stored(IJob job, int attempt = 1) => new()
    {
        Id = "job-1",
        Job = _serializer.Serialize(job),
        Queue = "q",
        State = JobState.Processing,
        CreatedAt = DateTimeOffset.UnixEpoch,
        RunAt = DateTimeOffset.UnixEpoch,
        Attempt = attempt,
        LastError = "previous",
    };

    private Task RunAsync(IJob job, CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        return _executor.ExecuteAsync(Stored(job), scope.ServiceProvider, _client, ct);
    }

    [Fact]
    public async Task Resolves_the_handler_from_the_scope_and_invokes_it()
    {
        await RunAsync(new Note("hello"));
        Assert.Contains("hello", _recorder.Calls);
    }

    [Fact]
    public async Task Passes_a_context_describing_the_job()
    {
        using var cts = new CancellationTokenSource();
        await RunAsync(new Note("ctx"), cts.Token);

        var context = Assert.Single(_recorder.Contexts);
        Assert.Equal("job-1", context.Id);
        Assert.Equal(1, context.Attempt);
        Assert.Equal("q", context.Queue);
        Assert.Equal("previous", context.LastError);
        Assert.Equal(cts.Token, context.CancellationToken);
        Assert.Same(_client, context.Jobs);
    }

    [Fact]
    public async Task Handlers_can_enqueue_follow_up_jobs()
    {
        await RunAsync(new EnqueueFollowUp("x"));
        var page = await _storage.QueryAsync(new Storage.JobQuery(), CancellationToken.None);
        var created = Assert.Single(page.Items);
        Assert.Equal("record", created.Job.Name);
        Assert.Contains("follow-up:x", created.Job.Payload);
    }

    [Fact]
    public async Task Rethrows_the_handler_exception_unwrapped()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(new AlwaysFail("kaboom")));
        Assert.Equal("kaboom", ex.Message);
    }

    [Fact]
    public async Task Missing_handler_is_a_JobExecutionException()
    {
        var ex = await Assert.ThrowsAsync<JobExecutionException>(() => RunAsync(new NoHandler()));
        Assert.Contains("NoHandlerHandler", ex.Message);
    }

    [Fact]
    public async Task Each_job_gets_the_scoped_handler_of_its_own_scope()
    {
        using var scope1 = _services.CreateScope();
        using var scope2 = _services.CreateScope();
        var h1 = scope1.ServiceProvider.GetRequiredService<IJobHandler<Note>>();
        var h2 = scope2.ServiceProvider.GetRequiredService<IJobHandler<Note>>();
        var h1Again = scope1.ServiceProvider.GetRequiredService<IJobHandler<FailTimes>>();

        Assert.NotSame(h1, h2);
        Assert.Same(h1, h1Again); // one handler instance per scope, shared across the job types it handles
    }
}
