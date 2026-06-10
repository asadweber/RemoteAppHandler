using Microsoft.Extensions.Options;

namespace HandelConsoleApp.Web.Services;

public sealed class AgentConnectionMonitor(
    RemoteAgentService agentService,
    IOptions<RemoteAgentOptions> options,
    ILogger<AgentConnectionMonitor> logger) : BackgroundService
{
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
                logger.LogDebug(ex, "Reconnect attempt failed, retrying in {Interval}s", interval.TotalSeconds);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
