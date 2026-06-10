using System.Text.Json;
using HandelApp.Shared.Protocol;

namespace HandelApp.Agent.Services;

public sealed class AppRegistryService : IDisposable
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly ILogger<AppRegistryService> _logger;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly Dictionary<string, AppDefinition> _apps = new(StringComparer.OrdinalIgnoreCase);

    public AppRegistryService(ILogger<AppRegistryService> logger)
    {
        _logger   = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "apps.json");
        Load();
    }

    public IReadOnlyList<AppDefinition> GetAll()
    {
        _rwLock.EnterReadLock();
        try { return [.. _apps.Values]; }
        finally { _rwLock.ExitReadLock(); }
    }

    public AppDefinition? Get(string appId)
    {
        _rwLock.EnterReadLock();
        try { return _apps.GetValueOrDefault(appId); }
        finally { _rwLock.ExitReadLock(); }
    }

    public (bool ok, string error) Register(AppDefinition def)
    {
        var (valid, reason) = Validate(def);
        if (!valid) return (false, reason);

        _rwLock.EnterWriteLock();
        try
        {
            if (_apps.ContainsKey(def.AppId))
                return (false, $"App '{def.AppId}' already registered. Unregister first to replace.");
            _apps[def.AppId] = def;
            Save();
            _logger.LogInformation("Registered app '{AppId}' ({Name})", def.AppId, def.DisplayName);
            return (true, string.Empty);
        }
        finally { _rwLock.ExitWriteLock(); }
    }

    public (bool ok, string error) Unregister(string appId)
    {
        _rwLock.EnterWriteLock();
        try
        {
            if (!_apps.Remove(appId))
                return (false, $"App '{appId}' not found");
            Save();
            _logger.LogInformation("Unregistered app '{AppId}'", appId);
            return (true, string.Empty);
        }
        finally { _rwLock.ExitWriteLock(); }
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<AppDefinition>>(json, _json);
            if (list is null) return;
            foreach (var def in list)
                _apps[def.AppId] = def;
            _logger.LogInformation("Loaded {Count} app(s) from {File}", _apps.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {File} — starting with empty app registry", _filePath);
        }
    }

    // Write to temp file then atomically replace — guards against corruption on crash
    private void Save()
    {
        var tmp = _filePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(_apps.Values.ToList(), _json);
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save {File}", _filePath);
        }
    }

    private static (bool ok, string reason) Validate(AppDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.AppId))
            return (false, "AppId is required");

        // Slug: lowercase letters, digits, hyphens — no path traversal possible
        if (!System.Text.RegularExpressions.Regex.IsMatch(def.AppId,
            @"^[a-z0-9][a-z0-9\-]{0,62}$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(100)))
            return (false, "AppId must be lowercase alphanumeric with hyphens (max 63 chars)");

        if (string.IsNullOrWhiteSpace(def.DisplayName))
            return (false, "DisplayName is required");
        if (string.IsNullOrWhiteSpace(def.DefaultInstancePath))
            return (false, "DefaultInstancePath is required");
        if (string.IsNullOrWhiteSpace(def.InstancesRootPath))
            return (false, "InstancesRootPath is required");
        if (string.IsNullOrWhiteSpace(def.ExecutableName))
            return (false, "ExecutableName is required");

        return (true, string.Empty);
    }

    public void Dispose() => _rwLock.Dispose();
}
