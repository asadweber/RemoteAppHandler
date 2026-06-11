namespace HandelApp.Agent.Services;

/// <summary>
/// Configuration options for the agent's TCP listener, bound from the <c>Agent</c>
/// section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Registered in DI via <c>services.Configure&lt;AgentOptions&gt;(configuration.GetSection("Agent"))</c>.
/// All properties have safe, localhost-only defaults so the agent works out-of-the-box
/// in development without any configuration file.
/// </remarks>
public sealed class AgentOptions
{
    /// <summary>
    /// TCP port the agent listens on for inbound Web → Agent commands.
    /// Default: <c>9876</c>.
    /// </summary>
    public int      ListenPort               { get; set; } = 9876;

    /// <summary>
    /// IP address to bind the TCP listener to.
    /// Default: <c>"127.0.0.1"</c> (loopback only — not reachable from remote hosts).
    /// Change to <c>"0.0.0.0"</c> only when the web application runs on a different machine
    /// and network security controls are in place.
    /// </summary>
    public string   BindAddress              { get; set; } = "127.0.0.1";

    /// <summary>
    /// Optional IP allowlist. When non-empty, only connections from listed addresses are accepted.
    /// An empty array (the default) permits all connecting IPs.
    /// </summary>
    public string[] AllowedClientIps         { get; set; } = [];

    /// <summary>
    /// Maximum number of pending connection requests in the TCP listener backlog.
    /// Default: <c>5</c>, sufficient for a single web application front-end.
    /// </summary>
    public int      MaxConcurrentConnections { get; set; } = 5;
}
