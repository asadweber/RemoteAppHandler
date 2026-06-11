using HandelApp.Shared.Protocol;

namespace HandelApp.Web.Models;

/// <summary>
/// View model for the <c>Apps/Index</c> view, which lists all registered applications
/// and provides a form for registering a new one.
/// </summary>
public sealed class AppsViewModel
{
    /// <summary>
    /// Whether the web app currently has an active TCP connection to the agent.
    /// Used by the view to show a connectivity warning banner.
    /// </summary>
    public bool                IsConnectedToAgent { get; set; }

    /// <summary>
    /// App definitions returned by the agent's <see cref="CommandType.ListApps"/> command.
    /// Empty when the agent is unreachable.
    /// </summary>
    public List<AppDefinition> Apps               { get; set; } = [];

    /// <summary>
    /// TempData message from the previous Post-Redirect-Get action (e.g. "App 'my-app' registered").
    /// <see langword="null"/> when no action was just performed.
    /// </summary>
    public string?             ResultMessage      { get; set; }

    /// <summary>
    /// <see langword="true"/> when <see cref="ResultMessage"/> describes a failure;
    /// drives the alert colour in the view.
    /// </summary>
    public bool                IsError            { get; set; }
}

/// <summary>
/// Form model bound from the "Register new app" form on <c>Apps/Index</c>.
/// Passed to <see cref="AppsController.Register"/> and converted to an
/// <see cref="AppDefinition"/> before being sent to the agent.
/// </summary>
public sealed class RegisterAppInputModel
{
    /// <summary>App identifier slug (lowercase alphanumeric, hyphens allowed).</summary>
    public string AppId               { get; set; } = string.Empty;

    /// <summary>Human-readable display name shown in the app list.</summary>
    public string DisplayName         { get; set; } = string.Empty;

    /// <summary>Absolute path to the default instance directory on the agent host.</summary>
    public string DefaultInstancePath { get; set; } = string.Empty;

    /// <summary>Absolute path to the root directory for numbered instance sub-folders.</summary>
    public string InstancesRootPath   { get; set; } = string.Empty;

    /// <summary>Executable file name within each instance folder (e.g. <c>MyApp.exe</c>).</summary>
    public string ExecutableName      { get; set; } = string.Empty;

    /// <summary>
    /// Prefix for numbered instance folder names. Defaults to <c>"Instance"</c>.
    /// The controller falls back to "Instance" when this is left blank.
    /// </summary>
    public string InstanceNamePrefix  { get; set; } = "Instance";
}
