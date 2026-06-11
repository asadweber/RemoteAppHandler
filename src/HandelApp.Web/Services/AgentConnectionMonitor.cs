using Microsoft.Extensions.Options;

namespace HandelApp.Web.Services;

/// <summary>
/// Background service that proactively maintains the TCP connection to the remote agent by
/// periodically calling <see cref="RemoteAgentService.EnsureConnectedAsync"/>.
/// </summary>
/// <remarks>
/// This service ensures that the connection is warm before a user action arrives, reducing
/// perceived latency. Reconnect failures are logged at Debug level and retried on the next
/// interval — the web app continues to function in a degraded state while the agent is offline.
/// </remarks>
public sealed class AgentConnectionMonitor(
    RemoteAgentService agentService,
    IOptions<RemoteAgentOptions> options,
    ILogger<AgentConnectionMonitor> logger) : BackgroundService
{
    /// <summary>
    /// Runs the reconnect loop until the host requests shutdown.
    /// </summary>
    /// <param name="stoppingToken">Signalled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.ReconnectIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await agentService.EnsureConnectedAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Connection failures are expected when the agent is offline — suppress noise.
                logger.LogDebug(ex, "Reconnect attempt failed, retrying in {Interval}s", interval.TotalSeconds);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
