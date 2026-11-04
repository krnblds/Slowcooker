using Slowcooker.Execution;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

public class JobSerializerTests
{
    private readonly JobTypeRegistry _registry = new();
    private readonly JobSerializer _serializer;

    public JobSerializerTests() => _serializer = new JobSerializer(_registry);

    [Fact]
    public void Uses_the_JobName_attribute_as_the_stored_name()
    {
        var job = _serializer.Serialize(new Note("hi"));

        Assert.Equal("record", job.Name);
        Assert.Equal("{\"Value\":\"hi\"}", job.Payload);
        Assert.Equal("record", job.DisplayName);
    }

    [Fact]
    public void Falls_back_to_a_version_free_CLR_name()
    {
        var job = _serializer.Serialize(new Unnamed(3));

        Assert.Equal("Slowcooker.Tests.Support.Unnamed, Slowcooker.Tests", job.Name);
        Assert.Equal("Unnamed", job.DisplayName);
        Assert.DoesNotContain("Version=", job.Name);
    }

    [Fact]
    public void Registered_names_resolve_without_loading_types_by_CLR_name()
    {
        _registry.Register(typeof(Note));
        var job = _serializer.Deserialize(new Job("record", "{\"Value\":\"x\"}"));
        Assert.Equal(new Note("x"), job);
    }

    [Fact]
    public void Unregistered_CLR_names_still_resolve_when_the_type_is_loadable()
    {
        var job = _serializer.Deserialize(new Job("Slowcooker.Tests.Support.Unnamed, Slowcooker.Tests", "{\"Number\":7}"));
        Assert.Equal(new Unnamed(7), job);
    }

    [Fact]
    public void Unknown_names_are_a_JobExecutionException()
    {
        var ex = Assert.Throws<JobExecutionException>(() => _serializer.Deserialize(new Job("ghost", "{}")));
        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public void Broken_payloads_are_a_JobExecutionException()
    {
        _registry.Register(typeof(Note));
        Assert.Throws<JobExecutionException>(() => _serializer.Deserialize(new Job("record", "{oops")));
        Assert.Throws<JobExecutionException>(() => _serializer.Deserialize(new Job("record", "null")));
    }

    [Fact]
    public void Roundtrips_complex_records()
    {
        _registry.Register(typeof(Complex));
        var original = new Complex(new Payload("p", 2), [4, 5], new Dictionary<string, decimal> { ["x"] = 9.5m }, new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)), Guid.NewGuid(), Color.Green, null);

        var roundtripped = (Complex)_serializer.Deserialize(_serializer.Serialize(original));

        Assert.Equal(original.Payload, roundtripped.Payload);
        Assert.Equal(original.Numbers, roundtripped.Numbers);
        Assert.Equal(original.Prices, roundtripped.Prices);
        Assert.Equal(original.When, roundtripped.When);
        Assert.Equal(original.Id, roundtripped.Id);
        Assert.Equal(Color.Green, roundtripped.Color);
        Assert.Null(roundtripped.Maybe);
    }

    [Fact]
    public void Enums_are_stored_as_strings_so_reordering_them_is_safe()
    {
        var job = _serializer.Serialize(new Complex(new Payload("p", 1), [], [], DateTimeOffset.UnixEpoch, Guid.Empty, Color.Green, 1));
        Assert.Contains("\"Color\":\"Green\"", job.Payload);
    }

    [Fact]
    public void Two_types_with_the_same_name_are_rejected()
    {
        _registry.Register(typeof(Note));
        Assert.Throws<InvalidOperationException>(() => _registry.Register(typeof(Clash)));
    }

    [Fact]
    public void Default_queue_comes_from_the_Queue_attribute()
    {
        Assert.Equal("payments", _serializer.DefaultQueue(typeof(ChargeCard)));
        Assert.Equal("default", _serializer.DefaultQueue(typeof(Note)));
    }

    [JobName("record")]
    private sealed record Clash : IJob;
}
