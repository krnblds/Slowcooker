using Slowcooker;
using Slowcooker.AspNetCore;
using Slowcooker.Samples.WebApi.Jobs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

// 1. Register Slowcooker: a storage, your handlers, and (in this process) a worker.
var slowcooker = builder.Services.AddSlowcooker()
    .AddHandlersFrom(typeof(Program).Assembly)
    .AddWorker(o =>
    {
        o.Queues = ["email", "default"];
        o.MaxConcurrency = 8;
        o.SucceededJobRetention = TimeSpan.FromMinutes(30); // keep the demo tidy
    });

// Storage is chosen by configuration so the sample runs with nothing installed (in-memory) or against docker-compose.
switch (builder.Configuration["Slowcooker:Storage"]?.ToLowerInvariant())
{
    case "postgres":
        slowcooker.UsePostgres(builder.Configuration.GetConnectionString("Postgres")!);
        break;
    case "redis":
        slowcooker.UseRedis(builder.Configuration.GetConnectionString("Redis")!);
        break;
    default:
        slowcooker.UseInMemoryStorage();
        break;
}

// Demo-only: fast retries and a trickle of generated jobs so the dashboard has something to show.
if (builder.Configuration.GetValue("Slowcooker:DemoTraffic", builder.Environment.IsDevelopment()))
{
    slowcooker.UseRetryPolicy(new DemoRetryPolicy());
    builder.Services.AddHostedService<DemoTraffic>();
}

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

// 2. Mount the dashboard API. Local requests only, unless you add .RequireAuthorization().
app.MapSlowcookerApi("/slowcooker");

// 3. Serve the built Vue dashboard (dashboard/ -> wwwroot/dashboard, see dashboard/README.md).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("/dashboard/{*path}", "dashboard/index.html");
app.MapGet("/", () => Results.Redirect("/dashboard/"));

// 4. Enqueue jobs from anywhere you can inject IJobClient.
app.MapPost("/signups", async (Signup signup, IJobClient jobs) =>
{
    var jobId = await jobs.EnqueueAsync(new SendWelcomeEmail(signup.Email, signup.Name));
    return Results.Accepted($"/slowcooker/jobs/{jobId}", new { jobId });
});

app.MapPost("/signups/later", async (Signup signup, IJobClient jobs) =>
{
    var jobId = await jobs.ScheduleAsync(new SendWelcomeEmail(signup.Email, signup.Name), TimeSpan.FromMinutes(1));
    return Results.Accepted($"/slowcooker/jobs/{jobId}", new { jobId });
});

// 5. Recurring jobs: a Cron schedule + time zone, declared once at startup. Safe to run on every instance.
using (var scope = app.Services.CreateScope())
{
    var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobClient>();
    await recurring.AddOrUpdateAsync("daily-report-eu", new GenerateDailyReport("eu"), Cron.Daily.At(6),
        new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London") });
    await recurring.AddOrUpdateAsync("report-every-2-min", new GenerateDailyReport("demo"), Cron.Every(TimeSpan.FromMinutes(2)));
}

app.Run();

public sealed record Signup(string Email, string Name);
