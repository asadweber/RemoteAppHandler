using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

/// <summary>
/// Top-level coordinator. Routes instance commands by AppId.
/// Owns one ProcessManagerRegistry + one InstanceManagerService per registered app.
/// </summary>
/// <remarks>
/// Each registered app gets an isolated "scope" — a (<see cref="ProcessManagerRegistry"/>,
/// <see cref="InstanceManagerService"/>) pair — constructed lazily on first access and
/// cached for the lifetime of the service. This ensures that configuration options derived
/// from an <see cref="AppDefinition"/> are not re-built on every command.
/// <para>
/// Thread-safety: mutations to <see cref="_apps"/> are guarded by <see cref="_lock"/>.
/// Individual app scopes are themselves thread-safe.
/// </para>
/// </remarks>
public sealed class MultiAppManagerService(
    AppRegistryService appRegistry,
    ILoggerFactory loggerFactory,
    ILogger<MultiAppManagerService> logger)
{
    /// <summary>
    /// Per-app runtime scope: process registry and instance manager, keyed by AppId (case-insensitive).
    /// </summary>
    private readonly Dictionary<string, (ProcessManagerRegistry Registry, InstanceManagerService Manager)> _apps
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // ── App registration commands ────────────────────────────────────────

    /// <summary>
    /// Registers a new application definition and creates its runtime scope.
    /// </summary>
    /// <param name="def">App definition supplied by the caller; validated inside <see cref="AppRegistryService"/>.</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> on success;
    /// <see cref="ResponseStatus.Error"/> when registration fails (e.g. duplicate AppId).
    /// </returns>
    public AgentResponse RegisterApp(AppDefinition def, string requestedBy)
    {
        var (ok, error) = appRegistry.Register(def);
        if (!ok) return Error(error);

        EnsureScope(def);
        logger.LogInformation("App '{AppId}' registered by {User}", def.AppId, requestedBy);
        return Ok($"App '{def.AppId}' registered");
    }

    /// <summary>
    /// Unregisters an application, refusing if any of its instances are still running.
    /// Removes the in-memory scope after successful persistence-layer removal.
    /// </summary>
    /// <param name="appId">Case-insensitive identifier of the app to remove.</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> on success;
    /// <see cref="ResponseStatus.Error"/> when instances are still running or app not found.
    /// </returns>
    public AgentResponse UnregisterApp(string appId, string requestedBy)
    {
        lock (_lock)
        {
            if (_apps.TryGetValue(appId, out var scope))
            {
                var running = scope.Registry.GetAll().Values.Any(m => m.IsRunning);
                if (running)
                    return Error($"Stop all running instances of '{appId}' before unregistering");
            }
        }

        var (ok, error) = appRegistry.Unregister(appId);
        if (!ok) return Error(error);

        lock (_lock) { _apps.Remove(appId); }
        logger.LogInformation("App '{AppId}' unregistered by {User}", appId, requestedBy);
        return Ok($"App '{appId}' unregistered");
    }

    /// <summary>
    /// Returns a list of all registered app definitions from the persistent registry.
    /// </summary>
    /// <returns>Response containing the full <see cref="AppDefinition"/> list.</returns>
    public AgentResponse ListApps()
    {
        var defs = appRegistry.GetAll();
        return new AgentResponse
        {
            Status  = ResponseStatus.Ok,
            Message = $"{defs.Count} app(s) registered",
            Apps    = [.. defs]
        };
    }

    // ── Instance commands (routed by AppId) ──────────────────────────────

    /// <summary>
    /// Creates a new numbered instance folder by copying the app's default instance directory.
    /// </summary>
    /// <param name="appId">Target app identifier.</param>
    /// <param name="number">Numeric suffix for the new instance (must be &gt; 0).</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> on success;
    /// <see cref="ResponseStatus.Error"/> or <see cref="ResponseStatus.AlreadyRunning"/> on failure.
    /// </returns>
    public AgentResponse CreateInstance(string appId, int number, string requestedBy)
    {
        var (mgr, err) = GetManager(appId);
        if (mgr is null) return Error(err!);
        return mgr.CreateInstance(number, requestedBy);
    }

    /// <summary>
    /// Deletes a numbered instance folder. Refuses if the instance is currently running.
    /// </summary>
    /// <param name="appId">Target app identifier.</param>
    /// <param name="number">Numeric suffix of the instance to delete.</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> on success;
    /// <see cref="ResponseStatus.Error"/> or <see cref="ResponseStatus.NotRunning"/> on failure.
    /// </returns>
    public AgentResponse DeleteInstance(string appId, int number, string requestedBy)
    {
        var (mgr, err) = GetManager(appId);
        if (mgr is null) return Error(err!);
        return mgr.DeleteInstance(number, requestedBy);
    }

    /// <summary>
    /// Returns status information for all known instances of an app (default + numbered).
    /// </summary>
    /// <param name="appId">Target app identifier.</param>
    /// <returns>Response containing the <see cref="AgentResponse.Instances"/> list.</returns>
    public AgentResponse ListInstances(string appId)
    {
        var (mgr, err) = GetManager(appId);
        if (mgr is null) return Error(err!);
        return mgr.ListInstances();
    }

    /// <summary>
    /// Starts the named instance's managed process.
    /// </summary>
    /// <param name="appId">Target app identifier.</param>
    /// <param name="instanceName">Exact instance name (e.g. "Default" or "Instance-3").</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>Start result from <see cref="ProcessManagerService.Start"/>.</returns>
    public AgentResponse StartInstance(string appId, string instanceName, string requestedBy)
    {
        var (reg, err) = GetRegistry(appId);
        if (reg is null) return Error(err!);
        return reg.GetOrCreate(instanceName).Start(requestedBy);
    }

    /// <summary>
    /// Stops the named instance's managed process, waiting up to the configured grace period
    /// before force-killing.
    /// </summary>
    /// <param name="appId">Target app identifier.</param>
    /// <param name="instanceName">Exact instance name.</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>Stop result from <see cref="ProcessManagerService.Stop"/>.</returns>
    public AgentResponse StopInstance(string appId, string instanceName, string requestedBy)
    {
        var (reg, err) = GetRegistry(appId);
        if (reg is null) return Error(err!);
        return reg.GetOrCreate(instanceName).Stop(requestedBy);
    }

    /// <summary>
    /// Retrieves the current run-state of a named instance.
    /// </summary>
    /// <param name="appId">Target app identifier.</param>
    /// <param name="instanceName">Exact instance name.</param>
    /// <returns>Status result from <see cref="ProcessManagerService.GetStatus"/>.</returns>
    public AgentResponse GetInstanceStatus(string appId, string instanceName)
    {
        var (reg, err) = GetRegistry(appId);
        if (reg is null) return Error(err!);
        return reg.GetOrCreate(instanceName).GetStatus();
    }

    /// <summary>
    /// Returns a <see cref="ProcessManagerService"/> for the "Default" instance of every
    /// registered app that has a <see cref="AppDefinition.DefaultInstancePath"/> set.
    /// Used by <see cref="DefaultInstanceStartupService"/> to auto-start defaults on agent boot.
    /// </summary>
    // Used by DefaultInstanceStartupService on agent startup
    public IReadOnlyList<(string AppId, ProcessManagerService Manager)> GetDefaultManagers()
    {
        var result = new List<(string, ProcessManagerService)>();
        foreach (var def in appRegistry.GetAll())
        {
            if (string.IsNullOrEmpty(def.DefaultInstancePath)) continue;
            var scope = EnsureScope(def);
            try { result.Add((def.AppId, scope.Registry.GetOrCreate("Default"))); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not get default manager for app '{AppId}'", def.AppId);
            }
        }
        return result;
    }

    // ── Internal helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Resolves the <see cref="ProcessManagerRegistry"/> for an app, returning an error tuple
    /// when the app is not registered.
    /// </summary>
    private (ProcessManagerRegistry? reg, string? error) GetRegistry(string appId)
    {
        var def = appRegistry.Get(appId);
        if (def is null) return (null, $"App '{appId}' not registered");
        return (EnsureScope(def).Registry, null);
    }

    /// <summary>
    /// Resolves the <see cref="InstanceManagerService"/> for an app, returning an error tuple
    /// when the app is not registered.
    /// </summary>
    private (InstanceManagerService? mgr, string? error) GetManager(string appId)
    {
        var def = appRegistry.Get(appId);
        if (def is null) return (null, $"App '{appId}' not registered");
        return (EnsureScope(def).Manager, null);
    }

    /// <summary>
    /// Returns the existing scope for an app, or creates and caches a new one.
    /// Building the scope involves constructing <see cref="AppOptions"/> from the
    /// <see cref="AppDefinition"/> and wiring up the <see cref="ProcessManagerRegistry"/>
    /// and <see cref="InstanceManagerService"/> together.
    /// </summary>
    /// <param name="def">The app definition providing path and name configuration.</param>
    private (ProcessManagerRegistry Registry, InstanceManagerService Manager) EnsureScope(AppDefinition def)
    {
        lock (_lock)
        {
            if (_apps.TryGetValue(def.AppId, out var existing))
                return existing;

            var opts = BuildOptions(def);
            var registry = new ProcessManagerRegistry(
                new OptionsWrapper<AppOptions>(opts),
                loggerFactory);
            var manager = new InstanceManagerService(
                new OptionsWrapper<AppOptions>(opts),
                registry,
                loggerFactory.CreateLogger<InstanceManagerService>());

            var scope = (registry, manager);
            _apps[def.AppId] = scope;
            return scope;
        }
    }

    /// <summary>
    /// Converts an <see cref="AppDefinition"/> (the persistent, user-facing model) into
    /// <see cref="AppOptions"/> (the runtime configuration consumed by services).
    /// The executable path is left empty here because <see cref="ProcessManagerRegistry"/>
    /// resolves per-instance exe paths at <c>GetOrCreate</c> time.
    /// </summary>
    private static AppOptions BuildOptions(AppDefinition def) => new()
    {
        DefaultInstancePath   = def.DefaultInstancePath,
        DefaultInstanceName   = "Default",
        InstancesRootPath     = def.InstancesRootPath,
        InstanceNamePrefix    = def.InstanceNamePrefix,
        ExecutableName        = def.ExecutableName,
        Arguments             = string.Empty,
        ShutdownGracePeriodMs = 10_000
    };

    /// <summary>Returns a standardised error response.</summary>
    private static AgentResponse Error(string msg) =>
        new() { Status = ResponseStatus.Error, Message = msg };

    /// <summary>Returns a standardised success response.</summary>
    private static AgentResponse Ok(string msg) =>
        new() { Status = ResponseStatus.Ok, Message = msg };
}
