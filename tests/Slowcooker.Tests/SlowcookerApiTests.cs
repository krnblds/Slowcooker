using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Slowcooker.AspNetCore;
using Slowcooker.Storage;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

public sealed class SlowcookerApiTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly WebApplication _app;
    private readonly HttpClient _http;
    private readonly IJobClient _client;
    private readonly IJobStorage _storage;
    private readonly IRecurringJobClient _recurring;

    private static readonly JsonSerializerOptions Json = SlowcookerApi.JsonOptions;

    private SlowcookerApiTests(bool remote, bool authorize = false, bool allowRemote = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSlowcooker().UseInMemoryStorage().AddHandler<TestHandlers>();
        if (authorize) builder.Services.AddAuthorization();
        _app = builder.Build();

        if (remote)
            _app.Use((ctx, next) => { ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9"); ctx.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.1"); return next(ctx); });
        if (authorize) { _app.UseAuthorization(); }

        var api = _app.MapSlowcookerApi("/api/jobs-admin", o => o.AllowUnauthenticatedRemoteAccess = allowRemote);
        if (authorize) api.RequireAuthorization(p => p.RequireAssertion(_ => true)); // "authorized" for the purpose of the filter
        _app.StartAsync().GetAwaiter().GetResult();

        _http = _app.GetTestClient();
        _client = _app.Services.GetRequiredService<IJobClient>();
        _storage = _app.Services.GetRequiredService<IJobStorage>();
        _recurring = _app.Services.GetRequiredService<IRecurringJobClient>();
    }

    public SlowcookerApiTests() : this(remote: false) { }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task Info_reports_version_storage_and_time()
    {
        var info = await GetAsync<InfoDto>("/api/jobs-admin/info");
        Assert.Equal("InMemoryJobStorage", info.Storage);
        Assert.Equal(Now, info.ServerTime);
        Assert.NotEqual("", info.Version);
    }

    [Fact]
    public async Task Stats_and_queues_reflect_storage()
    {
        await _client.EnqueueAsync(new Note("a"));
        await _client.EnqueueAsync(new ChargeCard("o1", 5));
        var fetched = await _storage.FetchAsync(["payments"], "w", TimeSpan.FromMinutes(1), 1, CancellationToken.None);
        await fetched[0].FailAsync("boom");
        await _recurring.AddOrUpdateAsync("nightly", new Note("n"), "0 0 * * *");

        var stats = await GetAsync<StatsDto>("/api/jobs-admin/stats");
        Assert.Equal(1, stats.Scheduled);
        Assert.Equal(1, stats.Failed);
        Assert.Equal(1, stats.Recurring);
        Assert.Equal(Now, stats.ServerTime);
        Assert.Equal(["default", "payments"], stats.Queues.Select(q => q.Name));

        var queues = await GetAsync<List<QueueDto>>("/api/jobs-admin/queues");
        Assert.Equal(new QueueDto("payments", 0, 0, 0, 1), queues.Single(q => q.Name == "payments"));
    }

    [Fact]
    public async Task Jobs_are_listed_newest_first_with_filters_and_paging()
    {
        for (var i = 0; i < 5; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await _client.EnqueueAsync(new Note($"n{i}"), new JobOptions { Queue = i % 2 == 0 ? "even" : "odd" });
        }

        var page = await GetAsync<PageDto<JobDto>>("/api/jobs-admin/jobs?take=2");
        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Take);
        Assert.Equal(["n4", "n3"], page.Items.Select(j => j.Payload.GetProperty("Value").GetString()));
        Assert.All(page.Items, j => Assert.Equal(JobState.Scheduled, j.State));

        var odd = await GetAsync<PageDto<JobDto>>("/api/jobs-admin/jobs?queue=odd&state=Scheduled");
        Assert.Equal(2, odd.Total);

        var none = await GetAsync<PageDto<JobDto>>("/api/jobs-admin/jobs?state=failed");
        Assert.Equal(0, none.Total);

        var bad = await _http.GetAsync("/api/jobs-admin/jobs?state=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Enum_values_are_camelCase_strings_on_the_wire()
    {
        await _client.EnqueueAsync(new Note("x"));
        var raw = await _http.GetStringAsync("/api/jobs-admin/jobs");
        Assert.Contains("\"state\":\"scheduled\"", raw);
        Assert.Contains("\"displayName\":\"record\"", raw);
    }

    [Fact]
    public async Task A_single_job_can_be_read_requeued_and_deleted()
    {
        var id = await _client.EnqueueAsync(new ChargeCard("o7", 12.5m));
        var fetched = await _storage.FetchAsync(["payments"], "w", TimeSpan.FromMinutes(1), 1, CancellationToken.None);
        await fetched[0].FailAsync("declined");

        var job = await GetAsync<JobDto>($"/api/jobs-admin/jobs/{id}");
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("declined", job.LastError);
        Assert.Equal("charge:o7", job.IdempotencyKey);
        Assert.Equal(12.5m, job.Payload.GetProperty("Amount").GetDecimal());

        var requeued = await _http.PostAsync($"/api/jobs-admin/jobs/{id}/requeue", null);
        Assert.Equal(HttpStatusCode.OK, requeued.StatusCode);
        var after = (await requeued.Content.ReadFromJsonAsync<JobDto>(Json))!;
        Assert.Equal(JobState.Scheduled, after.State);
        Assert.Equal(0, after.Attempt);

        var deleted = await _http.DeleteAsync($"/api/jobs-admin/jobs/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/jobs-admin/jobs/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/jobs-admin/jobs/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync($"/api/jobs-admin/jobs/{id}/requeue", null)).StatusCode);
    }

    [Fact]
    public async Task Requeueing_a_processing_job_is_a_conflict()
    {
        var id = await _client.EnqueueAsync(new Note("busy"));
        await _storage.FetchAsync(["default"], "w", TimeSpan.FromMinutes(1), 1, CancellationToken.None);

        var response = await _http.PostAsync($"/api/jobs-admin/jobs/{id}/requeue", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Recurring_jobs_can_be_listed_read_triggered_and_deleted()
    {
        await _recurring.AddOrUpdateAsync("nightly", new Note("n"), "0 3 * * *", new RecurringJobOptions { Queue = "reports" });

        var list = await GetAsync<List<RecurringJobDto>>("/api/jobs-admin/recurring");
        var dto = Assert.Single(list);
        Assert.Equal("nightly", dto.Id);
        Assert.Equal("0 3 * * *", dto.Cron);
        Assert.Equal("reports", dto.Queue);
        Assert.Equal(Now.Date.AddDays(1).AddHours(3), dto.NextRunAt!.Value.UtcDateTime);

        var single = await GetAsync<RecurringJobDto>("/api/jobs-admin/recurring/nightly");
        Assert.Equal(JsonSerializer.Serialize(dto, Json), JsonSerializer.Serialize(single, Json)); // JsonElement has no value equality

        var triggered = await _http.PostAsync("/api/jobs-admin/recurring/nightly/trigger", null);
        Assert.Equal(HttpStatusCode.Created, triggered.StatusCode);
        var created = (await triggered.Content.ReadFromJsonAsync<TriggeredDto>(Json))!;
        Assert.NotNull(await _storage.GetAsync(created.JobId, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync("/api/jobs-admin/recurring/nightly")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/jobs-admin/recurring/nightly")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync("/api/jobs-admin/recurring/nightly/trigger", null)).StatusCode);
    }

    [Fact]
    public async Task Cron_preview_lists_upcoming_runs_in_the_requested_time_zone()
    {
        var preview = await GetAsync<CronPreviewDto>("/api/jobs-admin/cron/preview?expression=0%206%20*%20*%20*&timeZone=Europe/London&count=3");
        Assert.True(preview.Valid);
        Assert.Equal("0 6 * * *", preview.Expression);
        Assert.Equal(3, preview.Upcoming.Count);
        Assert.Equal(new DateTimeOffset(2026, 6, 2, 5, 0, 0, TimeSpan.Zero), preview.Upcoming[0]); // 06:00 BST = 05:00 UTC
        Assert.Equal(preview.Upcoming[0].AddDays(1), preview.Upcoming[1]);

        var invalid = await GetAsync<CronPreviewDto>("/api/jobs-admin/cron/preview?expression=every%20tuesday");
        Assert.False(invalid.Valid);
        Assert.NotNull(invalid.Error);
        Assert.Empty(invalid.Upcoming);

        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/jobs-admin/cron/preview?expression=*%20*%20*%20*%20*&timeZone=Mars/Olympus")).StatusCode);
    }

    [Fact]
    public async Task Remote_requests_are_refused_by_default()
    {
        await using var remote = new SlowcookerApiTests(remote: true);
        var response = await remote._http.GetAsync("/api/jobs-admin/stats");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("RequireAuthorization", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Remote_requests_are_allowed_when_the_endpoints_require_authorization()
    {
        await using var remote = new SlowcookerApiTests(remote: true, authorize: true);
        Assert.Equal(HttpStatusCode.OK, (await remote._http.GetAsync("/api/jobs-admin/stats")).StatusCode);
    }

    [Fact]
    public async Task Remote_requests_are_allowed_when_explicitly_opted_in()
    {
        await using var remote = new SlowcookerApiTests(remote: true, allowRemote: true);
        Assert.Equal(HttpStatusCode.OK, (await remote._http.GetAsync("/api/jobs-admin/stats")).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
