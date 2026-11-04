using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slowcooker.AspNetCore;

namespace Slowcooker;

/// <summary>
/// The bundled dashboard: a compiled Vue app embedded in <c>Slowcooker.AspNetCore</c>, served together with its JSON API.
/// </summary>
public static class SlowcookerAdminUserInterface
{
    /// <summary>
    /// Serves the dashboard at <see cref="SlowcookerAdminOptions.Path"/> (default <c>/slowcooker</c>) and its API at
    /// <c>{Path}/api</c>. Nothing else is needed: the UI is mounted automatically after your middleware pipeline, so
    /// authentication, forwarded headers and the like have already run. Call <see cref="MapSlowcookerAdminUserInterface"/>
    /// instead if you want to choose where in your routing it lives.
    /// Without <see cref="SlowcookerAdminOptions.RequireAuthorization"/> it only answers local requests.
    /// </summary>
    public static SlowcookerBuilder AddAdminUserInterface(this SlowcookerBuilder builder, Action<SlowcookerAdminOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new SlowcookerAdminOptions();
        configure?.Invoke(options);

        builder.Services.AddRouting();
        builder.Services.Replace(ServiceDescriptor.Singleton(options));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, AutoMountFilter>());
        return builder;
    }

    /// <summary>
    /// Mounts the dashboard registered with <see cref="AddAdminUserInterface"/> into your endpoint routing explicitly, at
    /// <see cref="SlowcookerAdminOptions.Path"/>. Use this when your app has a catch-all route (for example a SPA fallback)
    /// that would otherwise shadow the automatic mount, or to add your own conventions to the returned group.
    /// </summary>
    public static RouteGroupBuilder MapSlowcookerAdminUserInterface(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetService<SlowcookerAdminOptions>()
            ?? throw new InvalidOperationException("Call AddAdminUserInterface() on AddSlowcooker() before MapSlowcookerAdminUserInterface().");
        options.ExplicitlyMapped = true;
        return Map(endpoints, options.Path, options);
    }

    private static RouteGroupBuilder Map(IEndpointRouteBuilder endpoints, string prefix, SlowcookerAdminOptions options)
    {
        var group = endpoints.MapGroup(prefix);
        group.AddEndpointFilter(new SlowcookerApi.AccessFilter(options.AllowUnauthenticatedRemoteAccess));

        SlowcookerApi.MapEndpoints(group.MapGroup("/api"));
        group.MapGet("/", EmbeddedDashboard.Index).ExcludeFromDescription();
        group.MapGet("/{**path}", EmbeddedDashboard.Asset).ExcludeFromDescription();

        if (options.AuthorizationPolicies is { } policies)
        {
            if (policies.Length == 0) group.RequireAuthorization();
            else group.RequireAuthorization(policies);
        }
        return group;
    }

    /// <summary>
    /// Appends a self-contained branch for the dashboard path after the application's own pipeline. Running last means
    /// it sees the request as the app does (forwarded headers applied, user authenticated) and never shadows app routes.
    /// </summary>
    private sealed class AutoMountFilter(SlowcookerAdminOptions options) : IStartupFilter
    {
        // WebApplication stores its global route builder under this key and UseRouting() in any branch would otherwise
        // reuse it, putting our endpoints in the app's own route table with the wrong path base. The branch needs its own.
        private const string GlobalEndpointRouteBuilderKey = "__GlobalEndpointRouteBuilder";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            if (options.ExplicitlyMapped) return;

            app.Map(options.Path, branch =>
            {
                branch.Properties.Remove(GlobalEndpointRouteBuilderKey);
                branch.UseRouting();
                if (options.AuthorizationPolicies is not null) branch.UseAuthorization();
                branch.UseEndpoints(endpoints => Map(endpoints, "", options));
            });
        };
    }
}
