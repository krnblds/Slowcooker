using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Slowcooker.Storage;

namespace Slowcooker.AspNetCore;

/// <summary>
/// The HTTP API behind a Slowcooker dashboard. Mount it with <see cref="MapSlowcookerApi"/>; the endpoint reference lives in
/// <c>docs/dashboard-api.md</c>. It uses its own JSON settings and does not touch your application's.
/// </summary>
public static class SlowcookerApi
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly string Version = typeof(IJobClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(IJobClient).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Maps the dashboard API under <paramref name="prefix"/> (default <c>/slowcooker</c>) and returns the group so you can
    /// add conventions such as <c>.RequireAuthorization()</c>. Without authorization metadata the API only answers local requests.
    /// </summary>
    public static RouteGroupBuilder MapSlowcookerApi(this IEndpointRouteBuilder endpoints, string prefix = "/slowcooker", Action<SlowcookerApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = new SlowcookerApiOptions();
        configure?.Invoke(options);

        var group = endpoints.MapGroup(prefix);
        group.AddEndpointFilter(new AccessFilter(options));

        group.MapGet("/info", (IJobStorage storage, TimeProvider time) =>
            Json(new InfoDto(Version, storage.GetType().Name, time.GetUtcNow())));

        group.MapGet("/stats", async (IJobMonitor monitor, TimeProvider time, CancellationToken ct) =>
        {
            var s = await monitor.GetStatsAsync(ct);
            return Json(new StatsDto(s.Scheduled, s.Processing, s.Succeeded, s.Failed, s.Recurring, s.Queues.Select(ToDto).ToList(), time.GetUtcNow()));
        });

        group.MapGet("/queues", async (IJobMonitor monitor, CancellationToken ct) =>
            Json((await monitor.GetStatsAsync(ct)).Queues.Select(ToDto).ToList()));

        group.MapGet("/jobs", async (IJobMonitor monitor, string? state, string? queue, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            JobState? parsedState = null;
            if (!string.IsNullOrWhiteSpace(state))
            {
                if (!Enum.TryParse<JobState>(state, ignoreCase: true, out var s))
                    return Json(new ErrorDto($"Unknown state '{state}'. Use scheduled, processing, succeeded or failed."), 400);
                parsedState = s;
            }

            var query = new JobQuery { State = parsedState, Queue = queue, Skip = skip, Take = take }.Normalized();
            var page = await monitor.QueryAsync(query, ct);
            return Json(new PageDto<JobDto>(page.Items.Select(JobDto.From).ToList(), page.Total, query.Skip, query.Take));
        });

        group.MapGet("/jobs/{id}", async (string id, IJobMonitor monitor, CancellationToken ct) =>
        {
            var job = await monitor.GetAsync(id, ct);
            return job is null ? NotFound("job", id) : Json(JobDto.From(job));
        });

        group.MapPost("/jobs/{id}/requeue", async (string id, IJobMonitor monitor, CancellationToken ct) =>
        {
            if (await monitor.RequeueAsync(id, ct))
                return Json(JobDto.From((await monitor.GetAsync(id, ct))!));

            var job = await monitor.GetAsync(id, ct);
            return job is null
                ? NotFound("job", id)
                : Json(new ErrorDto($"Job '{id}' is processing and cannot be requeued until it finishes."), 409);
        });

        group.MapDelete("/jobs/{id}", async (string id, IJobMonitor monitor, CancellationToken ct) =>
            await monitor.DeleteAsync(id, ct) ? Results.NoContent() : NotFound("job", id));

        group.MapGet("/recurring", async (IJobMonitor monitor, CancellationToken ct) =>
            Json((await monitor.ListRecurringAsync(ct)).Select(RecurringJobDto.From).ToList()));

        group.MapGet("/recurring/{id}", async (string id, IJobMonitor monitor, CancellationToken ct) =>
        {
            var job = await monitor.GetRecurringAsync(id, ct);
            return job is null ? NotFound("recurring job", id) : Json(RecurringJobDto.From(job));
        });

        group.MapPost("/recurring/{id}/trigger", async (string id, IJobMonitor monitor, CancellationToken ct) =>
        {
            var jobId = await monitor.TriggerRecurringAsync(id, ct);
            return jobId is null ? NotFound("recurring job", id) : Json(new TriggeredDto(jobId), 201);
        });

        group.MapDelete("/recurring/{id}", async (string id, IJobMonitor monitor, CancellationToken ct) =>
            await monitor.DeleteRecurringAsync(id, ct) ? Results.NoContent() : NotFound("recurring job", id));

        group.MapGet("/cron/preview", (TimeProvider time, string expression, string? timeZone = null, int count = 5) =>
        {
            TimeZoneInfo zone;
            try { zone = string.IsNullOrWhiteSpace(timeZone) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
            catch (TimeZoneNotFoundException) { return Json(new ErrorDto($"Unknown time zone '{timeZone}'."), 400); }
            catch (InvalidTimeZoneException) { return Json(new ErrorDto($"Invalid time zone '{timeZone}'."), 400); }

            count = Math.Clamp(count, 0, 100);
            if (!Cron.TryParse(expression, out var cron))
            {
                string? error = null;
                try { Cron.Parse(expression); } catch (ArgumentException ex) { error = ex.Message; }
                return Json(new CronPreviewDto(expression, false, error ?? "Invalid cron expression.", zone.Id, []));
            }

            return Json(new CronPreviewDto(cron!.Expression, true, null, zone.Id, cron.Upcoming(time.GetUtcNow(), zone, count)));
        });

        return group;
    }

    private static QueueDto ToDto(QueueStats q) => new(q.Name, q.Scheduled, q.Processing, q.Succeeded, q.Failed);

    private static IResult Json<T>(T value, int statusCode = 200) => Results.Json(value, JsonOptions, statusCode: statusCode);

    private static IResult NotFound(string kind, string id) => Json(new ErrorDto($"No {kind} with id '{id}'."), 404);

    /// <summary>
    /// Mirrors Hangfire's local-requests-only default: unauthenticated remote access to an API that can delete and
    /// requeue jobs is a security hole, so it is off unless explicitly enabled or the endpoint requires authorization.
    /// </summary>
    private sealed class AccessFilter(SlowcookerApiOptions options) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            if (options.AllowUnauthenticatedRemoteAccess || HasAuthorization(context.HttpContext) || IsLocal(context.HttpContext))
                return await next(context);

            return Json(new ErrorDto("The Slowcooker API only answers local requests unless you add .RequireAuthorization() to MapSlowcookerApi() or set AllowUnauthenticatedRemoteAccess."), 403);
        }

        private static bool HasAuthorization(HttpContext http)
            => http.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null
               && http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null;

        private static bool IsLocal(HttpContext http)
        {
            var remote = http.Connection.RemoteIpAddress;
            var local = http.Connection.LocalIpAddress;
            if (remote is null) return true; // in-process (TestServer) or unknown transport
            if (IPAddress.IsLoopback(remote)) return true;
            return local is not null && remote.Equals(local);
        }
    }
}
