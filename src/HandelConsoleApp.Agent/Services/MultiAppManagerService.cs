using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

/// <summary>
/// Top-level coordinator. Routes instance commands by AppId.
/// Owns one ProcessManagerRegistry + one InstanceManagerService per registered app.
/// </summary>
public sealed class MultiAppManagerService(
    AppRegistryService appRegistry,
    ILoggerFactory loggerFactory,
    ILogger<MultiAppManagerService> logger)
{
    private readonly Dictionary<string, (ProcessManagerRegistry Registry, InstanceManagerService Manager)> _apps
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // ── App registration commands ────────────────────────────────────────

    public AgentResponse RegisterApp(AppDefinition def, string requestedBy)
    {
        var (ok, error) = appRegistry.Register(def);
        if (!ok) return Error(error);

        EnsureScope(def);
        logger.LogInformation("App '{AppId}' registered by {User}", def.AppId, requestedBy);
        return Ok($"App '{def.AppId}' registered");
    }

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

    public AgentResponse CreateInstance(string appId, int number, string requestedBy)
    {
        var (mgr, err) = GetManager(appId);
        if (mgr is null) return Error(err!);
        return mgr.CreateInstance(number, requestedBy);
    }

    public AgentResponse DeleteInstance(string appId, int number, string requestedBy)
    {
        var (mgr, err) = GetManager(appId);
        if (mgr is null) return Error(err!);
        return mgr.DeleteInstance(number, requestedBy);
    }

    public AgentResponse ListInstances(string appId)
    {
        var (mgr, err) = GetManager(appId);
        if (mgr is null) return Error(err!);
        return mgr.ListInstances();
    }

    public AgentResponse StartInstance(string appId, string instanceName, string requestedBy)
    {
        var (reg, err) = GetRegistry(appId);
        if (reg is null) return Error(err!);
        return reg.GetOrCreate(instanceName).Start(requestedBy);
    }

    public AgentResponse StopInstance(string appId, string instanceName, string requestedBy)
    {
        var (reg, err) = GetRegistry(appId);
        if (reg is null) return Error(err!);
        return reg.GetOrCreate(instanceName).Stop(requestedBy);
    }

    public AgentResponse GetInstanceStatus(string appId, string instanceName)
    {
        var (reg, err) = GetRegistry(appId);
        if (reg is null) return Error(err!);
        return reg.GetOrCreate(instanceName).GetStatus();
    }

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

    private (ProcessManagerRegistry? reg, string? error) GetRegistry(string appId)
    {
        var def = appRegistry.Get(appId);
        if (def is null) return (null, $"App '{appId}' not registered");
        return (EnsureScope(def).Registry, null);
    }

    private (InstanceManagerService? mgr, string? error) GetManager(string appId)
    {
        var def = appRegistry.Get(appId);
        if (def is null) return (null, $"App '{appId}' not registered");
        return (EnsureScope(def).Manager, null);
    }

    private (ProcessManagerRegistry Registry, InstanceManagerService Manager) EnsureScope(AppDefinition def)
    {
        lock (_lock)
        {
            if (_apps.TryGetValue(def.AppId, out var existing))
                return existing;

            var opts = BuildOptions(def);
            var registry = new ProcessManagerRegistry(
                new OptionsWrapper<ConsoleAppOptions>(opts),
                loggerFactory);
            var manager = new InstanceManagerService(
                new OptionsWrapper<ConsoleAppOptions>(opts),
                registry,
                loggerFactory.CreateLogger<InstanceManagerService>());

            var scope = (registry, manager);
            _apps[def.AppId] = scope;
            return scope;
        }
    }

    private static ConsoleAppOptions BuildOptions(AppDefinition def) => new()
    {
        DefaultInstancePath   = def.DefaultInstancePath,
        DefaultInstanceName   = "Default",
        InstancesRootPath     = def.InstancesRootPath,
        InstanceNamePrefix    = def.InstanceNamePrefix,
        ExecutableName        = def.ExecutableName,
        Arguments             = string.Empty,
        ShutdownGracePeriodMs = 10_000
    };

    private static AgentResponse Error(string msg) =>
        new() { Status = ResponseStatus.Error, Message = msg };

    private static AgentResponse Ok(string msg) =>
        new() { Status = ResponseStatus.Ok, Message = msg };
}
