namespace HandelApp.Shared.Protocol;

/// <summary>
/// Persistent, user-facing definition of a managed application.
/// Registered with the agent via <see cref="CommandType.RegisterApp"/> and stored in
/// <c>apps.json</c> by <see cref="AppRegistryService"/>.
/// </summary>
/// <remarks>
/// <see cref="AppId"/> is the stable identifier used in all subsequent commands.
/// Runtime configuration is derived from this record by
/// <see cref="MultiAppManagerService"/> at command time.
/// </remarks>
public sealed record AppDefinition
{
    /// <summary>
    /// Lowercase alphanumeric slug (max 63 chars, hyphens allowed) that uniquely identifies
    /// the app within the agent. Validated by <see cref="AppRegistryService"/> on registration.
    /// </summary>
    public string AppId               { get; init; } = string.Empty;

    /// <summary>Human-readable name shown in the web UI.</summary>
    public string DisplayName         { get; init; } = string.Empty;

    /// <summary>
    /// Absolute path to the "Default" instance directory.
    /// Serves as the copy template when new numbered instances are created and as the
    /// working directory for the Default instance process.
    /// </summary>
    public string DefaultInstancePath { get; init; } = string.Empty;

    /// <summary>
    /// Root directory under which all numbered instance sub-folders are created.
    /// Must be writable by the agent process.
    /// </summary>
    public string InstancesRootPath   { get; init; } = string.Empty;

    /// <summary>
    /// File name of the executable within each instance directory (e.g. <c>MyApp.exe</c>).
    /// Combined with the instance path by <see cref="ProcessManagerRegistry"/> to resolve
    /// the full executable path.
    /// </summary>
    public string ExecutableName      { get; init; } = string.Empty;

    /// <summary>
    /// Prefix for numbered instance folder names (e.g. "Instance" → "Instance-1", "Instance-2").
    /// Default: <c>"Instance"</c>.
    /// </summary>
    public string InstanceNamePrefix  { get; init; } = "Instance";
}
