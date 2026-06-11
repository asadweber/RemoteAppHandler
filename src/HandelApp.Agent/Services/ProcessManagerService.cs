using System.Diagnostics;
using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

/// <summary>
/// Manages the lifecycle (start, stop, status) of a single named console-application instance.
/// Supports automatic restart on unexpected exits and deduplication of rogue duplicate processes
/// that share the same executable path.
/// </summary>
/// <remarks>
/// Thread-safety: all public and private methods that touch <see cref="_managedProcess"/> or
/// <see cref="_intentionalStop"/> acquire <see cref="_lock"/> first. The <see cref="OnProcessExited"/>
/// callback runs on a CLR thread-pool thread and must therefore re-acquire the lock before
/// mutating shared state.
/// </remarks>
public sealed class ProcessManagerService(
    IOptions<AppOptions> options,
    ILogger<ProcessManagerService> logger,
    string instanceName)
{
    private readonly AppOptions _options = options.Value;
    private Process? _managedProcess;
    private readonly object _lock = new();
    private bool _intentionalStop;   // true while Stop() is in progress — suppresses auto-restart

    /// <summary>
    /// Gets whether the managed process is currently alive.
    /// Reads <see cref="Process.HasExited"/> inside the instance lock to avoid races.
    /// </summary>
    public bool IsRunning
    {
        get { lock (_lock) { return _managedProcess is not null && !HasExitedSafe(_managedProcess); } }
    }

    /// <summary>
    /// Gets the OS process ID of the managed process, or <see langword="null"/> when not running.
    /// </summary>
    public int? ProcessId
    {
        get { lock (_lock) { return !HasExitedSafe(_managedProcess) ? _managedProcess!.Id : null; } }
    }

    /// <summary>
    /// Called at startup to attach to a process that is already running outside agent control.
    /// No-op if process not found or agent already tracks one.
    /// </summary>
    /// <remarks>
    /// This allows the agent to resume supervision of a process that survived a previous agent
    /// restart without needing to kill and re-launch it.
    /// </remarks>
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

    /// <summary>
    /// Starts the managed process. If a matching process is already running (tracked or discovered
    /// externally), returns <see cref="ResponseStatus.AlreadyRunning"/> without launching a second one.
    /// Any duplicate processes at the same executable path are killed before the new one launches.
    /// </summary>
    /// <param name="requestedBy">Identity of the caller, used for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> with PID on success;
    /// <see cref="ResponseStatus.AlreadyRunning"/> if already live;
    /// <see cref="ResponseStatus.Error"/> on launch failure.
    /// </returns>
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

    /// <summary>
    /// Stops the managed process gracefully via <see cref="Process.CloseMainWindow"/>, then
    /// force-kills the entire process tree if the process does not exit within
    /// <see cref="AppOptions.ShutdownGracePeriodMs"/> milliseconds.
    /// </summary>
    /// <param name="requestedBy">Identity of the caller, used for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> with "Stopped gracefully" or "Killed after timeout";
    /// <see cref="ResponseStatus.NotRunning"/> if the process was already stopped;
    /// <see cref="ResponseStatus.Error"/> on unexpected failure.
    /// </returns>
    /// <remarks>
    /// Sets <see cref="_intentionalStop"/> to <see langword="true"/> before sending the stop
    /// signal so that <see cref="OnProcessExited"/> does not trigger auto-restart.
    /// </remarks>
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

    /// <summary>
    /// Returns the current run-state of the managed process, attaching to any externally-started
    /// matching process and evicting duplicates as a side-effect.
    /// </summary>
    /// <returns>
    /// Always <see cref="ResponseStatus.Ok"/>; <see cref="AgentResponse.IsRunning"/> and
    /// <see cref="AgentResponse.ProcessId"/> reflect actual process state at call time.
    /// </returns>
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

    /// <summary>
    /// CLR thread-pool callback fired when the OS signals that the managed process has exited.
    /// Distinguishes intentional stops from crashes/user-closes and triggers auto-restart
    /// for the latter after a brief cool-down delay.
    /// </summary>
    // Called on CLR threadpool thread when OS signals process exit.
    private void OnProcessExited(object? sender, EventArgs e)
    {
        int exitCode;
        try { exitCode = _managedProcess?.ExitCode ?? -1; }
        catch (InvalidOperationException) { exitCode = -1; }

        bool wasIntentional;
        lock (_lock) { wasIntentional = _intentionalStop; }

        // No restart needed when the stop was requested by the agent or operator.
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

    /// <summary>
    /// Scans running processes for one whose exe path matches the configured executable.
    /// If found, adopts it as the managed process and subscribes to its exit event.
    /// Must be called inside <see cref="_lock"/>.
    /// </summary>
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

    /// <summary>
    /// Kills any extra processes at the same executable path that are NOT the one the agent
    /// currently tracks. Prevents split-brain scenarios where a manual launch creates a second
    /// instance the agent is unaware of.
    /// Must be called inside <see cref="_lock"/>.
    /// </summary>
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

    /// <summary>
    /// Safe wrapper around <see cref="Process.HasExited"/> that catches
    /// <see cref="InvalidOperationException"/> thrown when the process handle is released
    /// after <see cref="Process.WaitForExit()"/> completes.
    /// </summary>
    /// <param name="p">The process to check; <see langword="null"/> is treated as exited.</param>
    /// <returns><see langword="true"/> if the process has exited or is null; otherwise <see langword="false"/>.</returns>
    // Process.HasExited throws InvalidOperationException when the handle is released after WaitForExit.
    private static bool HasExitedSafe(Process? p)
    {
        if (p is null) return true;
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    /// <summary>
    /// Core launch logic: constructs a <see cref="ProcessStartInfo"/>, starts the process,
    /// subscribes to the exit event, and returns the result.
    /// Must be called inside <see cref="_lock"/>.
    /// </summary>
    /// <param name="requestedBy">
    /// Audit label — either a user identity or "auto-restart" for internally triggered restarts.
    /// </param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> with PID on success;
    /// <see cref="ResponseStatus.Error"/> with exception message on failure.
    /// </returns>
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
