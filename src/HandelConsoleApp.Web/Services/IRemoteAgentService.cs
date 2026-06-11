using HandelApp.Shared.Protocol;

namespace HandelApp.Web.Services;

/// <summary>
/// Abstraction over the TCP connection to the remote agent, allowing controllers to be
/// tested without a real agent process.
/// </summary>
public interface IRemoteAgentService
{
    /// <summary>
    /// Gets whether the service currently believes it has an open TCP connection to the agent.
    /// This is a best-effort flag; the connection may have dropped since last checked.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Sends a command to the agent and returns the response.
    /// </summary>
    /// <param name="command">Command to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agent's response.</returns>
    /// <exception cref="Exception">Thrown on connection or I/O failure.</exception>
    Task<AgentResponse> SendCommandAsync(AgentCommand command, CancellationToken ct = default);
}
