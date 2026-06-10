using System.Diagnostics;
using HandelConsoleApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelConsoleApp.Agent.Services;

public sealed class ProcessManagerService(
    IOptions<ConsoleAppOptions> options,
    ILogger<ProcessManagerService> logger)
{
    private readonly ConsoleAppOptions _options = options.Value;
    private Process? _managedProcess;
    private readonly object _lock = new();

    public bool IsRunning
    {
        get { lock (_lock) { return _managedProcess is { HasExited: false }; } }
    }

    public int? ProcessId
    {
        get { lock (_lock) { return _managedProcess is { HasExited: false } ? _managedProcess.Id : null; } }
    }

    public AgentResponse Start(string requestedBy)
    {
        lock (_lock)
        {
            if (_managedProcess is { HasExited: false })
            {
                return new AgentResponse
                {
                    Status    = ResponseStatus.AlreadyRunning,
                    Message   = $"Process already running (PID {_managedProcess.Id})",
                    IsRunning = true,
                    ProcessId = _managedProcess.Id
                };
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = _options.ExecutablePath,
                    WorkingDirectory       = _options.WorkingDirectory,
                    Arguments              = _options.Arguments,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };

                _managedProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _managedProcess.Exited += (_, _) =>
                    logger.LogInformation("Managed process exited at {Time}", DateTime.UtcNow);

                _managedProcess.Start();

                logger.LogInformation("Started {Exe} (PID {Pid}) on behalf of {User}",
                    _options.ExecutablePath, _managedProcess.Id, requestedBy);

                return new AgentResponse
                {
                    Status    = ResponseStatus.Ok,
                    Message   = $"Started successfully (PID {_managedProcess.Id})",
                    IsRunning = true,
                    ProcessId = _managedProcess.Id
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start process");
                return new AgentResponse
                {
                    Status  = ResponseStatus.Error,
                    Message = $"Failed to start: {ex.Message}"
                };
            }
        }
    }

    public AgentResponse Stop(string requestedBy)
    {
        lock (_lock)
        {
            if (_managedProcess is null || _managedProcess.HasExited)
            {
                return new AgentResponse
                {
                    Status    = ResponseStatus.NotRunning,
                    Message   = "Process is not running",
                    IsRunning = false
                };
            }

            try
            {
                _managedProcess.CloseMainWindow();
                bool exited = _managedProcess.WaitForExit(_options.ShutdownGracePeriodMs);

                if (!exited)
                {
                    _managedProcess.Kill(entireProcessTree: true);
                    logger.LogWarning("Process killed after grace period. RequestedBy: {User}", requestedBy);
                }
                else
                {
                    logger.LogInformation("Process stopped gracefully. RequestedBy: {User}", requestedBy);
                }

                return new AgentResponse
                {
                    Status    = ResponseStatus.Ok,
                    Message   = exited ? "Stopped gracefully" : "Killed after timeout",
                    IsRunning = false
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stop process");
                return new AgentResponse
                {
                    Status  = ResponseStatus.Error,
                    Message = $"Failed to stop: {ex.Message}"
                };
            }
        }
    }

    public AgentResponse GetStatus() => new()
    {
        Status    = ResponseStatus.Ok,
        IsRunning = IsRunning,
        ProcessId = ProcessId,
        Message   = IsRunning ? $"Running (PID {ProcessId})" : "Not running"
    };
}
