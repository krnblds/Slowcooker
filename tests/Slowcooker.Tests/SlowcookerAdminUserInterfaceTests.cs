using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Slowcooker.AspNetCore;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

public sealed class SlowcookerAdminUserInterfaceTests
{
    private sealed class Host : IAsyncDisposable
    {
        public readonly WebApplication App;
        public readonly HttpClient Http;

        public Host(Action<SlowcookerAdminOptions>? configure = null, bool remote = false, bool authorization = false, Action<WebApplication>? pipeline = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSlowcooker().UseInMemoryStorage().AddHandler<TestHandlers>().AddAdminUserInterface(configure);
            if (authorization) builder.Services.AddAuthorization(o => o.AddPolicy("ops", p => p.RequireAssertion(_ => true)));
            App = builder.Build();

            if (remote)
                App.Use((ctx, next) => { ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9"); ctx.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.1"); return next(ctx); });
            pipeline?.Invoke(App);
            App.StartAsync().GetAwaiter().GetResult();
            Http = App.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dashboard_is_served_at_the_default_path_without_any_pipeline_code()
    {
        await using var host = new Host();
        var page = await host.Http.GetAsync("/slowcooker/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"app\">", html);

        // Assets are referenced relative to the page so the mount path can be anything.
        var script = Regex.Match(html, "src=\"\\./(assets/[^\"]+\\.js)\"").Groups[1].Value;
        Assert.NotEqual("", script);
        var asset = await host.Http.GetAsync("/slowcooker/" + script);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
        Assert.True(asset.Headers.CacheControl?.Public);
        Assert.Contains("immutable", asset.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Page_carries_a_base_href_for_the_mount_point_and_serves_client_side_routes()
    {
        await using var host = new Host();
        var root = await host.Http.GetStringAsync("/slowcooker/");
        Assert.Contains("<base href=\"/slowcooker/\">", root);

        var deep = await host.Http.GetAsync("/slowcooker/jobs/01abc");
        Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
        Assert.Equal("text/html", deep.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<base href=\"/slowcooker/\">", await deep.Content.ReadAsStringAsync());

        // Unknown API paths and unknown files stay 404 rather than becoming the page.
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/slowcooker/api/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/slowcooker/jobs/logo.png")).StatusCode);
    }

    [Fact]
    public async Task Base_href_includes_the_application_path_base_and_the_explicit_mount()
    {
        await using var behindProxy = new Host(o => o.Path = "/ops", pipeline: app => app.UsePathBase("/app"));
        Assert.Contains("<base href=\"/app/ops/\">", await behindProxy.Http.GetStringAsync("/app/ops/recurring"));

        await using var mapped = new Host(pipeline: app => app.MapSlowcookerAdminUserInterface());
        Assert.Contains("<base href=\"/slowcooker/\">", await mapped.Http.GetStringAsync("/slowcooker/jobs/1"));
    }

    [Fact]
    public async Task Path_without_trailing_slash_redirects_to_the_page()
    {
        await using var host = new Host();
        var response = await host.Http.GetAsync("/slowcooker?tab=jobs");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/slowcooker/?tab=jobs", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Api_lives_under_the_dashboard_path()
    {
        await using var host = new Host();
        var info = await host.Http.GetFromJsonAsync<InfoDto>("/slowcooker/api/info", SlowcookerApi.JsonOptions);
        Assert.Equal("InMemoryJobStorage", info!.Storage);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/slowcooker/api/jobs/nope")).StatusCode);
    }

    [Fact]
    public async Task Unknown_files_and_other_paths_are_not_served()
    {
        await using var host = new Host();
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/slowcooker/assets/missing.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/slowcookerx/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Mount_path_is_configurable()
    {
        await using var host = new Host(o => o.Path = "admin/jobs/");
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/admin/jobs/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/admin/jobs/api/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/slowcooker/")).StatusCode);
    }

    [Fact]
    public void Root_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SlowcookerAdminOptions { Path = "/" });
    }

    [Fact]
    public async Task App_routes_are_not_shadowed_and_run_before_the_dashboard()
    {
        await using var host = new Host(pipeline: app => app.MapGet("/slowcooker/mine", () => "app"));
        Assert.Equal("app", await host.Http.GetStringAsync("/slowcooker/mine"));
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/slowcooker/")).StatusCode);
    }

    [Fact]
    public async Task Explicit_map_wins_over_a_catch_all_route_and_is_not_mounted_twice()
    {
        await using var host = new Host(pipeline: app =>
        {
            app.MapSlowcookerAdminUserInterface();
            app.MapFallback(() => "spa");
        });
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/slowcooker/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/slowcooker/api/stats")).StatusCode);
        Assert.Equal("spa", await host.Http.GetStringAsync("/elsewhere"));
    }

    [Fact]
    public async Task Catch_all_route_shadows_the_automatic_mount()
    {
        // Documented: with a SPA fallback, call MapSlowcookerAdminUserInterface() explicitly.
        await using var host = new Host(pipeline: app => app.MapFallback(() => "spa"));
        Assert.Equal("spa", await host.Http.GetStringAsync("/slowcooker/"));
    }

    [Fact]
    public async Task Explicit_map_without_registration_explains_what_to_do()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSlowcooker().UseInMemoryStorage();
        var app = builder.Build();
        var ex = Assert.Throws<InvalidOperationException>(() => app.MapSlowcookerAdminUserInterface());
        Assert.Contains("AddAdminUserInterface", ex.Message);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Remote_requests_are_refused_by_default()
    {
        await using var host = new Host(remote: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Http.GetAsync("/slowcooker/")).StatusCode);
        var api = await host.Http.GetAsync("/slowcooker/api/stats");
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
        Assert.Contains("AddAdminUserInterface", await api.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Remote_requests_are_allowed_when_authorization_is_required()
    {
        await using var host = new Host(o => o.RequireAuthorization("ops"), remote: true, authorization: true);
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/slowcooker/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/slowcooker/api/stats")).StatusCode);
    }

    [Fact]
    public async Task Remote_requests_are_allowed_when_explicitly_opted_in()
    {
        await using var host = new Host(o => o.AllowUnauthenticatedRemoteAccess = true, remote: true);
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/slowcooker/api/stats")).StatusCode);
    }

    [Fact]
    public async Task Requiring_authorization_without_authorization_services_fails_at_startup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSlowcooker().UseInMemoryStorage().AddAdminUserInterface(o => o.RequireAuthorization());
        await using var app = builder.Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
    }
}
