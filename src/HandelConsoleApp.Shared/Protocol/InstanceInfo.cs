namespace HandelApp.Shared.Protocol;

/// <summary>
/// Snapshot of a single console-application instance's identity and current run-state.
/// Returned as part of <see cref="AgentResponse.Instances"/> by the
/// <see cref="CommandType.ListInstances"/> command.
/// </summary>
public sealed record InstanceInfo
{
    /// <summary>Logical name of the instance (e.g. "Default", "Instance-1").</summary>
    public string InstanceName { get; init; } = string.Empty;

    /// <summary>Absolute path to the instance's directory on the agent host.</summary>
    public string FolderPath   { get; init; } = string.Empty;

    /// <summary>
    /// Whether the instance's managed process is currently alive at the time the
    /// response was created.
    /// </summary>
    public bool   IsRunning    { get; init; }

    /// <summary>OS process ID, or <see langword="null"/> when the process is not running.</summary>
    public int?   ProcessId    { get; init; }

    /// <summary>
    /// <see langword="true"/> for the "Default" instance; <see langword="false"/> for
    /// all numbered instances. Used by the web UI to suppress the delete button on default.
    /// </summary>
    public bool   IsDefault    { get; init; }
}
