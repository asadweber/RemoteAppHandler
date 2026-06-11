using System.Text.Json;
using HandelApp.Shared.Protocol;

namespace HandelApp.Agent.Services;

/// <summary>
/// Persistent registry of all <see cref="AppDefinition"/> entries known to the agent.
/// Definitions are loaded from <c>apps.json</c> on construction and saved back atomically
/// after every mutation.
/// </summary>
/// <remarks>
/// Thread-safety: a <see cref="ReaderWriterLockSlim"/> allows concurrent reads while
/// serialising writes. Callers on different threads may call <see cref="GetAll"/> and
/// <see cref="Get"/> concurrently without contention.
/// <para>
/// Persistence: the backing file is written to a <c>.tmp</c> sibling first, then renamed
/// over the real file, so a crash mid-write cannot corrupt the registry.
/// </para>
/// </remarks>
public sealed class AppRegistryService : IDisposable
{
    /// <summary>
    /// Shared JSON serializer options — pretty-printed for human readability of <c>apps.json</c>.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>Absolute path to the <c>apps.json</c> persistence file beside the agent executable.</summary>
    private readonly string _filePath;
    private readonly ILogger<AppRegistryService> _logger;

    /// <summary>Guards concurrent access to <see cref="_apps"/>.</summary>
    private readonly ReaderWriterLockSlim _rwLock = new();

    /// <summary>
    /// In-memory dictionary of registered apps keyed by <see cref="AppDefinition.AppId"/>.
    /// Case-insensitive to match the slug validation rules enforced by <see cref="Validate"/>.
    /// </summary>
    private readonly Dictionary<string, AppDefinition> _apps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialises the registry and eagerly loads persisted definitions from disk.
    /// </summary>
    /// <param name="logger">Logger for informational and error messages.</param>
    public AppRegistryService(ILogger<AppRegistryService> logger)
    {
        _logger   = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "apps.json");
        Load();
    }

    /// <summary>
    /// Returns a snapshot of all currently registered app definitions.
    /// </summary>
    /// <returns>Immutable list; safe to iterate without holding the registry lock.</returns>
    public IReadOnlyList<AppDefinition> GetAll()
    {
        _rwLock.EnterReadLock();
        try { return [.. _apps.Values]; }
        finally { _rwLock.ExitReadLock(); }
    }

    /// <summary>
    /// Looks up a single app definition by its identifier.
    /// </summary>
    /// <param name="appId">Case-insensitive app identifier.</param>
    /// <returns>The matching <see cref="AppDefinition"/>, or <see langword="null"/> if not found.</returns>
    public AppDefinition? Get(string appId)
    {
        _rwLock.EnterReadLock();
        try { return _apps.GetValueOrDefault(appId); }
        finally { _rwLock.ExitReadLock(); }
    }

    /// <summary>
    /// Validates and registers a new app definition. The definition is persisted to disk
    /// immediately after a successful in-memory write.
    /// </summary>
    /// <param name="def">The app definition to register. Must pass all <see cref="Validate"/> checks.</param>
    /// <returns>
    /// <c>(true, "")</c> on success;
    /// <c>(false, reason)</c> when validation fails or the AppId already exists.
    /// </returns>
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

    /// <summary>
    /// Removes an app definition from the registry and persists the updated state to disk.
    /// </summary>
    /// <param name="appId">Case-insensitive identifier of the app to remove.</param>
    /// <returns>
    /// <c>(true, "")</c> on success;
    /// <c>(false, reason)</c> when the app does not exist.
    /// </returns>
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

    /// <summary>
    /// Reads <c>apps.json</c> and populates <see cref="_apps"/>. Called once from the constructor
    /// before the service is published to the DI container, so no locking is required here.
    /// A missing file is treated as an empty registry (first-run scenario).
    /// </summary>
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
    /// <summary>
    /// Serialises the current in-memory registry to disk using a write-to-temp-then-rename
    /// strategy to prevent file corruption if the process dies mid-write.
    /// Must be called while the write lock is held.
    /// </summary>
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

    /// <summary>
    /// Validates the required fields of an <see cref="AppDefinition"/> and enforces the
    /// slug format for <see cref="AppDefinition.AppId"/> to prevent path-traversal attacks.
    /// </summary>
    /// <param name="def">The definition to validate.</param>
    /// <returns><c>(true, "")</c> when valid; <c>(false, reason)</c> on the first violation found.</returns>
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

    /// <inheritdoc/>
    public void Dispose() => _rwLock.Dispose();
}
