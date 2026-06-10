using Microsoft.Extensions.Options;

namespace HandelConsoleApp.Agent.Services;

public sealed class DefaultInstanceStartupService(
    ProcessManagerRegistry registry,
    IOptions<ConsoleAppOptions> options,
    ILogger<DefaultInstanceStartupService> logger) : BackgroundService
{
    private readonly ConsoleAppOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_opts.DefaultInstanceName) ||
            string.IsNullOrEmpty(_opts.DefaultInstancePath) ||
            !Directory.Exists(_opts.DefaultInstancePath))
        {
            logger.LogWarning("Default instance path '{Path}' not found — skipping auto-start", _opts.DefaultInstancePath);
            return;
        }

        // Brief delay so TcpCommandListener binds first and logs appear in order
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            var manager = registry.GetOrCreate(_opts.DefaultInstanceName);

            if (manager.IsRunning)
            {
                logger.LogInformation("Default instance '{Name}' already running (PID {Pid})",
                    _opts.DefaultInstanceName, manager.ProcessId);
                return;
            }

            var response = manager.Start("agent-startup");
            if (response.Status == HandelConsoleApp.Shared.Protocol.ResponseStatus.Ok)
                logger.LogInformation("Default instance '{Name}' started on agent startup (PID {Pid})",
                    _opts.DefaultInstanceName, response.ProcessId);
            else
                logger.LogWarning("Default instance '{Name}' failed to start: {Msg}",
                    _opts.DefaultInstanceName, response.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error auto-starting default instance '{Name}'", _opts.DefaultInstanceName);
        }
    }
}
