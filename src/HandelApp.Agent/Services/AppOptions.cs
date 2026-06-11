namespace HandelApp.Agent.Services;

/// <summary>
/// Runtime configuration for a single console-application instance managed by
/// <see cref="ProcessManagerService"/>. Derived at runtime from an <see cref="HandelApp.Shared.Protocol.AppDefinition"/>
/// by <see cref="MultiAppManagerService"/> and <see cref="ProcessManagerRegistry"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="AgentOptions"/>, this class is not bound directly from
/// <c>appsettings.json</c>. It is constructed programmatically so that each instance
/// gets its own resolved exe path and working directory while sharing app-level settings
/// such as <see cref="Arguments"/> and <see cref="ShutdownGracePeriodMs"/>.
/// </remarks>
public sealed class AppOptions
{
    /// <summary>
    /// Full path to the executable that <see cref="ProcessManagerService"/> will launch.
    /// Resolved per-instance by <see cref="ProcessManagerRegistry"/>.
    /// </summary>
    public string ExecutablePath        { get; set; } = string.Empty;

    /// <summary>
    /// Working directory for the launched process. Typically the same folder as <see cref="ExecutablePath"/>.
    /// </summary>
    public string WorkingDirectory      { get; set; } = string.Empty;

    /// <summary>
    /// Command-line arguments passed to the executable on launch.
    /// Empty string means no extra arguments.
    /// </summary>
    public string Arguments             { get; set; } = string.Empty;

    /// <summary>
    /// How long (in milliseconds) <see cref="ProcessManagerService.Stop"/> waits for the
    /// process to exit after sending <c>CloseMainWindow</c> before force-killing it.
    /// Default: <c>10 000</c> ms (10 seconds).
    /// </summary>
    public int    ShutdownGracePeriodMs { get; set; } = 10_000;

    // Multi-instance support

    /// <summary>
    /// Absolute path to the "Default" instance directory. Used as the copy source when
    /// <see cref="InstanceManagerService.CreateInstance"/> provisions a new numbered instance.
    /// </summary>
    public string DefaultInstancePath { get; set; } = string.Empty;

    /// <summary>
    /// The logical name of the default instance (e.g. "Default").
    /// Used to identify it in <see cref="ProcessManagerRegistry"/> and to prevent accidental deletion.
    /// </summary>
    public string DefaultInstanceName { get; set; } = "Default";

    /// <summary>
    /// Root directory that contains all numbered instance sub-folders.
    /// All instance paths must resolve within this directory (enforced by
    /// <see cref="InstanceManagerService.IsContained"/>).
    /// </summary>
    public string InstancesRootPath   { get; set; } = string.Empty;

    /// <summary>
    /// Prefix used for numbered instance folder names (e.g. "Instance" → "Instance-1", "Instance-2").
    /// </summary>
    public string InstanceNamePrefix  { get; set; } = "Instance";

    /// <summary>
    /// File name of the executable within each instance directory (e.g. "MyApp.exe").
    /// Combined with the instance path by <see cref="ProcessManagerRegistry"/>
    /// to form the full <see cref="ExecutablePath"/>.
    /// </summary>
    public string ExecutableName      { get; set; } = string.Empty;
}
