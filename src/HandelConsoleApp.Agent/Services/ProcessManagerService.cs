using System.Diagnostics;
using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

public sealed class ProcessManagerService(
    IOptions<ConsoleAppOptions> options,
    ILogger<ProcessManagerService> logger,
    string instanceName)
{
    private readonly ConsoleAppOptions _options = options.Value;
    private Process? _managedProcess;
    private readonly object _lock = new();
    private bool _intentionalStop;   // true while Stop() is in progress — suppresses auto-restart

    public bool IsRunning
    {
        get { lock (_lock) { return _managedProcess is not null && !HasExitedSafe(_managedProcess); } }
    }

    public int? ProcessId
    {
        get { lock (_lock) { return !HasExitedSafe(_managedProcess) ? _managedProcess!.Id : null; } }
    }

    /// <summary>
    /// Called at startup to attach to a process that is already running outside agent control.
    /// No-op if process not found or agent already tracks one.
    /// </summary>
    public void TryAttachExisting()
    {
        lock (_lock)
        {
            if (_managedProcess is not null && !HasExitedSafe(_managedProcess))
                return;   // already tracking a live process

            var exeFull = Path.GetFullPath(_options.ExecutablePath);
            var exeName = Path.GetFileNameWithoutExtension(exeFull);

            Process? match = null;
            try
            {
                foreach (var p in Process.GetProcessesByName(exeName))
                {
                    try
                    {
                        // MainModule requires elevated rights on some OS configs — skip on access denied
                        var modulePath = p.MainModule?.FileName;
                        if (modulePath is not null &&
                            string.Equals(Path.GetFullPath(modulePath), exeFull, StringComparison.OrdinalIgnoreCase))
                        {
                            match = p;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        p.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[{Instance}] Could not scan for existing process", instanceName);
                return;
            }

            if (match is null)
                return;

            _intentionalStop     = false;
            _managedProcess      = match;
            _managedProcess.EnableRaisingEvents = true;
            _managedProcess.Exited += OnProcessExited;

            logger.LogInformation("[{Instance}] Attached to existing process (PID {Pid})", instanceName, match.Id);
        }
    }

    public AgentResponse Start(string requestedBy)
    {
        lock (_lock)
        {
            // Attach to externally-started process before deciding to launch a new one
            AttachExistingUnderLock();
            KillDuplicatesUnderLock();

            if (_managedProcess is not null && !HasExitedSafe(_managedProcess))
            {
                return new AgentResponse
                {
                    Status    = ResponseStatus.AlreadyRunning,
                    Message   = $"Process already running (PID {_managedProcess.Id})",
                    IsRunning = true,
                    ProcessId = _managedProcess.Id
                };
            }

            _intentionalStop = false;
            return StartInternal(requestedBy);
        }
    }

    public AgentResponse Stop(string requestedBy)
    {
        lock (_lock)
        {
            // Attach to externally-started process so we can stop it
            AttachExistingUnderLock();
            KillDuplicatesUnderLock();

            if (HasExitedSafe(_managedProcess))
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
                _intentionalStop = true;

                _managedProcess.CloseMainWindow();
                bool exited = _managedProcess.WaitForExit(_options.ShutdownGracePeriodMs);

                if (!exited)
                {
                    _managedProcess.Kill(entireProcessTree: true);
                    logger.LogWarning("[{Instance}] Process killed after grace period. RequestedBy: {User}", instanceName, requestedBy);
                }
                else
                {
                    logger.LogInformation("[{Instance}] Process stopped gracefully. RequestedBy: {User}", instanceName, requestedBy);
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
                _intentionalStop = false;
                logger.LogError(ex, "[{Instance}] Failed to stop process", instanceName);
                return new AgentResponse
                {
                    Status  = ResponseStatus.Error,
                    Message = $"Failed to stop: {ex.Message}"
                };
            }
        }
    }

    public AgentResponse GetStatus()
    {
        lock (_lock)
        {
            AttachExistingUnderLock();
            KillDuplicatesUnderLock();

            bool running = _managedProcess is not null && !HasExitedSafe(_managedProcess);
            int? pid     = running ? _managedProcess!.Id : null;
            return new AgentResponse
            {
                Status    = ResponseStatus.Ok,
                IsRunning = running,
                ProcessId = pid,
                Message   = running ? $"Running (PID {pid})" : "Not running"
            };
        }
    }

    // Called on CLR threadpool thread when OS signals process exit.
    private void OnProcessExited(object? sender, EventArgs e)
    {
        int exitCode;
        try { exitCode = _managedProcess?.ExitCode ?? -1; }
        catch (InvalidOperationException) { exitCode = -1; }

        bool wasIntentional;
        lock (_lock) { wasIntentional = _intentionalStop; }

        if (wasIntentional)
        {
            logger.LogInformation("[{Instance}] Process exited as requested (exit code {Code})", instanceName, exitCode);
            return;
        }

        if (exitCode == 0)
            logger.LogInformation("[{Instance}] Process closed by user (exit code 0) — restarting", instanceName);
        else
            logger.LogWarning("[{Instance}] Process crashed (exit code {Code}) — restarting", instanceName, exitCode);

        // Brief delay to avoid tight restart loop if exe is broken
        Thread.Sleep(3_000);

        lock (_lock)
        {
            // Double-check: someone may have called Start() or Stop() during the delay
            if (_intentionalStop || !HasExitedSafe(_managedProcess))
                return;

            var result = StartInternal("auto-restart");
            if (result.Status == ResponseStatus.Ok)
                logger.LogInformation("[{Instance}] Auto-restarted (PID {Pid})", instanceName, result.ProcessId);
            else
                logger.LogError("[{Instance}] Auto-restart failed: {Msg}", instanceName, result.Message);
        }
    }

    // Scans running processes for one whose exe path matches — must be called inside _lock.
    private void AttachExistingUnderLock()
    {
        if (_managedProcess is not null && !HasExitedSafe(_managedProcess))
            return;

        var exeFull = Path.GetFullPath(_options.ExecutablePath);
        var exeName = Path.GetFileNameWithoutExtension(exeFull);

        try
        {
            foreach (var p in Process.GetProcessesByName(exeName))
            {
                try
                {
                    var modulePath = p.MainModule?.FileName;
                    if (modulePath is not null &&
                        string.Equals(Path.GetFullPath(modulePath), exeFull, StringComparison.OrdinalIgnoreCase))
                    {
                        _intentionalStop             = false;
                        _managedProcess              = p;
                        _managedProcess.EnableRaisingEvents = true;
                        _managedProcess.Exited      += OnProcessExited;
                        logger.LogInformation("[{Instance}] Attached to existing process (PID {Pid})", instanceName, p.Id);
                        return;
                    }
                    else
                    {
                        p.Dispose();
                    }
                }
                catch (Exception)
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{Instance}] Could not scan for existing process", instanceName);
        }
    }

    // Kills any extra processes at the same exe path that are NOT the one agent tracks.
    // Must be called inside _lock.
    private void KillDuplicatesUnderLock()
    {
        if (HasExitedSafe(_managedProcess))
            return;

        var exeFull  = Path.GetFullPath(_options.ExecutablePath);
        var exeName  = Path.GetFileNameWithoutExtension(exeFull);
        var ownedPid = _managedProcess.Id;

        try
        {
            foreach (var p in Process.GetProcessesByName(exeName))
            {
                if (p.Id == ownedPid) { p.Dispose(); continue; }
                try
                {
                    var modulePath = p.MainModule?.FileName;
                    if (modulePath is not null &&
                        string.Equals(Path.GetFullPath(modulePath), exeFull, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill(entireProcessTree: true);
                        logger.LogWarning("[{Instance}] Killed duplicate process (PID {Pid}) — agent owns PID {Owned}",
                            instanceName, p.Id, ownedPid);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "[{Instance}] Could not kill duplicate PID {Pid}", instanceName, p.Id);
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{Instance}] Error scanning for duplicates", instanceName);
        }
    }

    // Process.HasExited throws InvalidOperationException when the handle is released after WaitForExit.
    private static bool HasExitedSafe(Process? p)
    {
        if (p is null) return true;
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    // Must be called inside _lock.
    private AgentResponse StartInternal(string requestedBy)
    {
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
            _managedProcess.Exited += OnProcessExited;

            _managedProcess.Start();

            logger.LogInformation("[{Instance}] Started {Exe} (PID {Pid}) by {User}",
                instanceName, _options.ExecutablePath, _managedProcess.Id, requestedBy);

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
            logger.LogError(ex, "[{Instance}] Failed to start process", instanceName);
            return new AgentResponse
            {
                Status  = ResponseStatus.Error,
                Message = $"Failed to start: {ex.Message}"
            };
        }
    }
}
