using System.Collections.Frozen;
using System.Reflection;
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

    public static bool IsAvailable => Files.ContainsKey("index.html");

    public static IResult Index(HttpContext http)
    {
        // Assets are referenced relatively (./assets/...), so the page must live at a URL with a trailing slash.
        var path = http.Request.Path.Value ?? "";
        if (!path.EndsWith('/'))
            return Results.Redirect(http.Request.PathBase + path + "/" + http.Request.QueryString, permanent: false);

        if (!IsAvailable)
        {
            return Results.Text(
                "The Slowcooker dashboard was not compiled into this build of Slowcooker.AspNetCore. " +
                "Run `npm run build` in dashboard/ and rebuild, or install the released package.",
                statusCode: StatusCodes.Status404NotFound);
        }

        http.Response.Headers.CacheControl = "no-cache";
        return File("index.html", "text/html; charset=utf-8");
    }

    public static IResult Asset(HttpContext http, string path)
    {
        if (!Files.ContainsKey(path)) return Results.NotFound();

        // Vite fingerprints everything under assets/, so those can be cached forever.
        http.Response.Headers.CacheControl = path.StartsWith("assets/", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        return File(path, ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream");
    }

    private static IResult File(string path, string contentType)
        => Results.Stream(Assembly.GetManifestResourceStream(Files[path])!, contentType);
}
