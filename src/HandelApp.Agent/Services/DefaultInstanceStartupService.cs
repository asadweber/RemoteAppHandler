namespace HandelApp.Agent.Services;

/// <summary>
/// .NET Generic Host background service that auto-starts the "Default" instance of every
/// registered application when the agent process boots.
/// </summary>
/// <remarks>
/// A deliberate 2-second startup delay allows <see cref="TcpCommandListener"/> to bind its
/// TCP port first, so that inbound commands can be received before startup completes —
/// useful when an orchestrator sends commands immediately after the agent process starts.
/// <para>
/// This service is intentionally tolerant of per-app failures: an exception for one app
/// is logged and skipped so that the remaining apps still get started.
/// </para>
/// </remarks>
public sealed class DefaultInstanceStartupService(
    MultiAppManagerService multiAppManager,
    ILogger<DefaultInstanceStartupService> logger) : BackgroundService
{
    /// <summary>
    /// Waits briefly for the TCP listener to be ready, then starts the default instance
    /// of each registered app that is not already running.
    /// </summary>
    /// <param name="stoppingToken">Signalled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief delay so TcpCommandListener binds first
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        foreach (var (appId, manager) in multiAppManager.GetDefaultManagers())
        {
            try
            {
                // Skip apps whose default instance is already running (e.g. survived a previous agent restart).
                if (manager.IsRunning)
                {
                    logger.LogInformation("[{AppId}] Default already running (PID {Pid})", appId, manager.ProcessId);
                    continue;
                }

                var response = manager.Start("agent-startup");
                if (response.Status == HandelApp.Shared.Protocol.ResponseStatus.Ok)
                    logger.LogInformation("[{AppId}] Default started on agent startup (PID {Pid})", appId, response.ProcessId);
                else
                    logger.LogWarning("[{AppId}] Default failed to start: {Msg}", appId, response.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{AppId}] Error auto-starting default instance", appId);
            }
        }
    }
}
