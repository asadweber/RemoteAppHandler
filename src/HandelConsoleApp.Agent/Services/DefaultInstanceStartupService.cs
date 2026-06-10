namespace HandelApp.Agent.Services;

public sealed class DefaultInstanceStartupService(
    MultiAppManagerService multiAppManager,
    ILogger<DefaultInstanceStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief delay so TcpCommandListener binds first
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        foreach (var (appId, manager) in multiAppManager.GetDefaultManagers())
        {
            try
            {
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
