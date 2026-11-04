using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Slowcooker.AspNetCore;

/// <summary>
/// The compiled Vue dashboard, embedded in this assembly as <c>ui/...</c> resources at build time (see the csproj).
/// Consumers get the UI with the package; nothing is read from disk.
/// </summary>
internal static class EmbeddedDashboard
{
    private const string Prefix = "ui/";
    private static readonly Assembly Assembly = typeof(EmbeddedDashboard).Assembly;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Relative path (forward slashes) to manifest resource name.</summary>
    private static readonly FrozenDictionary<string, string> Files = Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
        .ToFrozenDictionary(n => n[Prefix.Length..].Replace('\\', '/'), n => n, StringComparer.Ordinal);

    private static readonly Lazy<string?> IndexHtml = new(() =>
    {
        if (!Files.TryGetValue("index.html", out var name)) return null;
        using var reader = new StreamReader(Assembly.GetManifestResourceStream(name)!, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    public static bool IsAvailable => IndexHtml.Value is not null;

    /// <summary>The dashboard root. Redirects to the trailing-slash form so relative asset URLs resolve.</summary>
    public static IResult Index(HttpContext http, SlowcookerAdminOptions options)
    {
        var path = http.Request.Path.Value ?? "";
        if (!path.EndsWith('/'))
            return Results.Redirect(http.Request.PathBase + path + "/" + http.Request.QueryString, permanent: false);
        return Page(http, options);
    }

    /// <summary>
    /// A file from the build, or, for a path without an extension (a client-side route such as <c>jobs/123</c>),
    /// the page itself so deep links work. Unknown API paths and unknown files are 404.
    /// </summary>
    public static IResult Asset(HttpContext http, SlowcookerAdminOptions options, string path)
    {
        if (Files.ContainsKey(path))
        {
            // Vite fingerprints everything under assets/, so those can be cached forever.
            http.Response.Headers.CacheControl = path.StartsWith("assets/", StringComparison.Ordinal)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
            return Results.Stream(Assembly.GetManifestResourceStream(Files[path])!, ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream");
        }

        var isRoute = !path.StartsWith("api/", StringComparison.Ordinal) && !Path.HasExtension(path.AsSpan().TrimEnd('/'));
        return isRoute ? Page(http, options) : Results.NotFound();
    }

    private static IResult Page(HttpContext http, SlowcookerAdminOptions options)
    {
        var html = IndexHtml.Value;
        if (html is null)
        {
            return Results.Text(
                "The Slowcooker dashboard was not compiled into this build of Slowcooker.AspNetCore. " +
                "Run `npm run build` in dashboard/ and rebuild, or install the released package.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // The page is served for every client-side route, so assets and API calls must resolve against the mount
        // point rather than the current URL. Mounted automatically, PathBase already includes the dashboard path;
        // mapped explicitly, it does not.
        var pathBase = http.Request.PathBase.Value ?? "";
        var mount = pathBase.EndsWith(options.Path, StringComparison.OrdinalIgnoreCase) ? pathBase : pathBase + options.Path;
        var withBase = html.Replace("<head>", $"<head><base href=\"{mount}/\">", StringComparison.OrdinalIgnoreCase);

        http.Response.Headers.CacheControl = "no-cache";
        return Results.Content(withBase, "text/html; charset=utf-8");
    }
}
