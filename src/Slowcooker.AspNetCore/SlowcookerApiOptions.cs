namespace Slowcooker.AspNetCore;

public sealed class SlowcookerApiOptions
{
    /// <summary>
    /// By default the API answers only requests from the local machine unless the endpoints carry authorization metadata
    /// (for example <c>app.MapSlowcookerApi().RequireAuthorization("ops")</c>). Set this to true to serve remote callers
    /// without authorization. Do not do that on anything reachable from the internet: the API can requeue and delete jobs.
    /// </summary>
    public bool AllowUnauthenticatedRemoteAccess { get; set; }
}
