namespace HandelApp.Web.Services;

/// <summary>
/// Configuration for the web application's connection to the remote agent TCP service,
/// bound from the <c>RemoteAgent</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class RemoteAgentOptions
{
    /// <summary>
    /// Hostname or IP address of the machine running the agent.
    /// Default: <c>"localhost"</c> — assumes agent and web app run on the same machine.
    /// </summary>
    public string Host                     { get; set; } = "localhost";

    /// <summary>
    /// TCP port the agent is listening on. Must match <see cref="AgentOptions.ListenPort"/>.
    /// Default: <c>9876</c>.
    /// </summary>
    public int    Port                     { get; set; } = 9876;

    /// <summary>
    /// Maximum seconds to wait when establishing a new TCP connection to the agent.
    /// Default: <c>5</c> seconds.
    /// </summary>
    public int    ConnectTimeoutSeconds    { get; set; } = 5;

    /// <summary>
    /// Maximum seconds to wait for the agent to respond to a command.
    /// Stop commands may approach this limit on a hung process; set generously.
    /// Default: <c>30</c> seconds.
    /// </summary>
    public int    CommandTimeoutSeconds    { get; set; } = 30;

    /// <summary>
    /// How often <see cref="AgentConnectionMonitor"/> polls to re-establish a dropped connection.
    /// Default: <c>15</c> seconds.
    /// </summary>
    public int    ReconnectIntervalSeconds { get; set; } = 15;
}
