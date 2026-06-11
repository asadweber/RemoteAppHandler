using HandelApp.Shared.Protocol;

namespace HandelApp.Web.Models;

/// <summary>
/// View model for the <c>ConsoleApp/Index</c> view, which lists all instances of a
/// specific registered application and their run-states.
/// </summary>
public sealed class InstancesViewModel
{
    /// <summary>
    /// Whether the web app currently has an active TCP connection to the agent.
    /// Used by the view to show a connectivity warning banner.
    /// </summary>
    public bool               IsConnectedToAgent { get; set; }

    /// <summary>
    /// Instance snapshots returned by the agent's <see cref="CommandType.ListInstances"/> command.
    /// Empty when the agent is unreachable.
    /// </summary>
    public List<InstanceInfo> Instances          { get; set; } = [];

    /// <summary>
    /// TempData message from the previous Post-Redirect-Get action (e.g. "Instance-2 started").
    /// <see langword="null"/> when no action was just performed.
    /// </summary>
    public string?            ResultMessage      { get; set; }

    /// <summary>
    /// <see langword="true"/> when <see cref="ResultMessage"/> describes a failure;
    /// drives the alert colour in the view.
    /// </summary>
    public bool               IsError            { get; set; }
}
