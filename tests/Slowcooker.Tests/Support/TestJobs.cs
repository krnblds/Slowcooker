using System.Collections.Concurrent;

namespace Slowcooker.Tests.Support;

/// <summary>Shared, DI-registered recorder so handlers can report back to the test.</summary>
public sealed class Recorder
{
    public ConcurrentQueue<string> Calls { get; } = new();
    public ConcurrentDictionary<string, int> Counters { get; } = new();
    public ConcurrentDictionary<string, TaskCompletionSource> Gates { get; } = new();
    public ConcurrentDictionary<string, TaskCompletionSource> Started { get; } = new();
    public ConcurrentQueue<JobContext> Contexts { get; } = new();

    public int Increment(string key) => Counters.AddOrUpdate(key, 1, (_, n) => n + 1);
    public TaskCompletionSource Gate(string key) => Gates.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    public TaskCompletionSource StartedSignal(string key) => Started.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

// ---- Jobs (data) ----

[JobName("record")]
public sealed record Note(string Value) : IJob;

[JobName("fail-times")]
public sealed record FailTimes(string Key, int Times) : IJob;

[JobName("always-fail")]
public sealed record AlwaysFail(string Message) : IJob;

[JobName("wait-for-gate")]
public sealed record WaitForGate(string Key) : IJob;

[JobName("charge-card")]
[Queue("payments")]
public sealed record ChargeCard(string OrderId, decimal Amount) : IJob
{
    public string IdempotencyKey => $"charge:{OrderId}";
}

public sealed record Unnamed(int Number) : IJob;

[JobName("enqueue-follow-up")]
public sealed record EnqueueFollowUp(string Value) : IJob;

[JobName("no-handler")]
public sealed record NoHandler : IJob;

[JobName("complex")]
public sealed record Complex(Payload Payload, List<int> Numbers, Dictionary<string, decimal> Prices, DateTimeOffset When, Guid Id, Color Color, int? Maybe) : IJob;

public sealed record Payload(string Name, int Count);

public enum Color { Red, Green }

// ---- Handlers (behaviour) ----

public sealed class TestHandlers(Recorder recorder) :
    IJobHandler<Note>,
    IJobHandler<FailTimes>,
    IJobHandler<AlwaysFail>,
    IJobHandler<WaitForGate>,
    IJobHandler<ChargeCard>,
    IJobHandler<Unnamed>,
    IJobHandler<EnqueueFollowUp>,
    IJobHandler<Complex>
{
    public Task HandleAsync(Note job, JobContext context)
    {
        recorder.Calls.Enqueue(job.Value);
        recorder.Contexts.Enqueue(context);
        return Task.CompletedTask;
    }

    public Task HandleAsync(FailTimes job, JobContext context)
    {
        var n = recorder.Increment(job.Key);
        if (n <= job.Times) throw new InvalidOperationException($"transient failure {n}");
        recorder.Calls.Enqueue($"{job.Key}:ok");
        return Task.CompletedTask;
    }

    public Task HandleAsync(AlwaysFail job, JobContext context)
    {
        recorder.Increment(job.Message);
        throw new InvalidOperationException(job.Message);
    }

    public async Task HandleAsync(WaitForGate job, JobContext context)
    {
        recorder.StartedSignal(job.Key).TrySetResult();
        try
        {
            await recorder.Gate(job.Key).Task.WaitAsync(context.CancellationToken);
            recorder.Calls.Enqueue($"{job.Key}:released");
        }
        catch (OperationCanceledException)
        {
            recorder.Calls.Enqueue($"{job.Key}:cancelled");
            throw;
        }
    }

    public Task HandleAsync(ChargeCard job, JobContext context)
    {
        recorder.Calls.Enqueue($"charged:{job.OrderId}:{job.Amount}");
        return Task.CompletedTask;
    }

    public Task HandleAsync(Unnamed job, JobContext context)
    {
        recorder.Calls.Enqueue($"unnamed:{job.Number}");
        return Task.CompletedTask;
    }

    public async Task HandleAsync(EnqueueFollowUp job, JobContext context)
    {
        await context.Jobs.EnqueueAsync(new Note($"follow-up:{job.Value}"), cancellationToken: context.CancellationToken);
    }

    public Task HandleAsync(Complex job, JobContext context)
    {
        recorder.Calls.Enqueue($"{job.Payload.Name}/{job.Payload.Count}|{string.Join(",", job.Numbers)}|{string.Join(",", job.Prices.Select(p => $"{p.Key}={p.Value}"))}|{job.When:O}|{job.Id}|{job.Color}|{job.Maybe?.ToString() ?? "null"}");
        return Task.CompletedTask;
    }
}
