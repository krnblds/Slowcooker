namespace Slowcooker.AspNetCore;

/// <summary>Settings for the bundled dashboard registered with <c>AddAdminUserInterface()</c>.</summary>
public sealed class SlowcookerAdminOptions
{
    private string _path = "/slowcooker";

    /// <summary>
    /// Where the dashboard is served (default <c>/slowcooker</c>). The JSON API lives underneath it at <c>{Path}/api</c>.
    /// Must be a non-root path; a trailing slash is ignored.
    /// </summary>
    public string Path
    {
        get => _path;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var normalized = "/" + value.Trim().Trim('/');
            if (normalized == "/")
                throw new ArgumentException("The dashboard cannot be mounted at the root path; use something like \"/slowcooker\".", nameof(value));
            _path = normalized;
        }
    }

    /// <summary>
    /// Serve remote callers without authorization. Off by default: without <see cref="RequireAuthorization"/> the dashboard
    /// and its API only answer requests from the local machine, because the API can requeue and delete jobs.
    /// </summary>
    public bool AllowUnauthenticatedRemoteAccess { get; set; }

    internal string[]? AuthorizationPolicies { get; private set; }

    internal bool ExplicitlyMapped { get; set; }

    /// <summary>
    /// Protects the dashboard and its API with ASP.NET Core authorization: the default policy, or the named policies.
    /// This is the setting to use on anything reachable from the internet. Requires <c>AddAuthorization()</c> and, in
    /// a Startup-style app, <c>UseAuthentication()</c> in the pipeline.
    /// </summary>
    public SlowcookerAdminOptions RequireAuthorization(params string[] policyNames)
    {
        ArgumentNullException.ThrowIfNull(policyNames);
        AuthorizationPolicies = policyNames;
        return this;
    }
}
