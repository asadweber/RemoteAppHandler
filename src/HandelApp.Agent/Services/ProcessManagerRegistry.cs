using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

/// <summary>
/// Thread-safe factory and cache of <see cref="ProcessManagerService"/> instances,
/// one per named instance of a single registered application.
/// </summary>
/// <remarks>
/// Managers are created lazily on first access and reused for all subsequent calls,
/// ensuring that a single <see cref="ProcessManagerService"/> holds the authoritative
/// process handle for each instance.
/// <para>
/// Security: instance names are validated against a strict allowlist regex before any
/// filesystem path is constructed, preventing directory traversal via crafted names.
/// The resolved executable path is also checked with <see cref="InstanceManagerService.IsContained"/>
/// to ensure it stays within the instance folder.
/// </para>
/// </remarks>
public sealed class ProcessManagerRegistry(
    IOptions<ConsoleAppOptions> options,
    ILoggerFactory loggerFactory)
{
    private readonly ConsoleAppOptions _opts = options.Value;

    /// <summary>
    /// Cached managers keyed by instance name (case-insensitive).
    /// </summary>
    private readonly Dictionary<string, ProcessManagerService> _managers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Returns the <see cref="ProcessManagerService"/> for the given instance name,
    /// creating and caching a new one if it does not yet exist.
    /// </summary>
    /// <param name="instanceName">
    /// Either <see cref="ConsoleAppOptions.DefaultInstanceName"/> (e.g. "Default") or
    /// a numbered name matching the pattern <c>{InstanceNamePrefix}-\d+</c>.
    /// </param>
    /// <returns>The cached or newly created manager for the instance.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="instanceName"/> does not match the required format,
    /// or when the resolved paths escape the expected directories.
    /// </exception>
    public ProcessManagerService GetOrCreate(string instanceName)
    {
        bool isDefault = string.Equals(instanceName, _opts.DefaultInstanceName, StringComparison.OrdinalIgnoreCase);

        // Default instance bypasses name format validation — its name is configuration-controlled.
        if (!isDefault)
            ValidateInstanceName(instanceName);

        lock (_lock)
        {
            if (_managers.TryGetValue(instanceName, out var existing))
                return existing;

            string instancePath;
            if (isDefault)
            {
                instancePath = _opts.DefaultInstancePath;
            }
            else
            {
                instancePath = Path.Combine(_opts.InstancesRootPath, instanceName);

                // Containment check — prevents path traversal reaching outside InstancesRootPath
                if (!InstanceManagerService.IsContained(instancePath, _opts.InstancesRootPath, out var reason))
                    throw new ArgumentException(reason, nameof(instanceName));
            }

            var exePath = Path.Combine(instancePath, _opts.ExecutableName);

            // Ensure exe path stays inside instance folder
            if (!InstanceManagerService.IsContained(exePath, instancePath, out var exeReason))
                throw new ArgumentException(exeReason, nameof(instanceName));

            // Build per-instance options — working directory and exe path are instance-specific,
            // while arguments and grace period are shared from the app-level configuration.
            var instanceOpts = new ConsoleAppOptions
            {
                ExecutablePath        = exePath,
                WorkingDirectory      = instancePath,
                Arguments             = _opts.Arguments,
                ShutdownGracePeriodMs = _opts.ShutdownGracePeriodMs
            };

            var manager = new ProcessManagerService(
                new OptionsWrapper<ConsoleAppOptions>(instanceOpts),
                loggerFactory.CreateLogger<ProcessManagerService>(),
                instanceName);

            _managers[instanceName] = manager;
            return manager;
        }
    }

    /// <summary>
    /// Returns a snapshot of all currently cached managers.
    /// Safe to iterate without holding the registry lock.
    /// </summary>
    /// <returns>Dictionary copy keyed by instance name.</returns>
    public IReadOnlyDictionary<string, ProcessManagerService> GetAll()
    {
        lock (_lock) { return new Dictionary<string, ProcessManagerService>(_managers); }
    }

    // Strict allowlist: only "Prefix-N" where N is one or more digits
    /// <summary>
    /// Validates that <paramref name="instanceName"/> matches the pattern
    /// <c>{InstanceNamePrefix}-\d+</c>, ruling out names that could form
    /// path-traversal sequences (e.g. <c>Instance-../secret</c>).
    /// </summary>
    /// <param name="instanceName">Name to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the name does not match the expected format.</exception>
    private void ValidateInstanceName(string instanceName)
    {
        var pattern = $@"^{Regex.Escape(_opts.InstanceNamePrefix)}-\d+$";
        if (!Regex.IsMatch(instanceName, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException($"Invalid instance name: '{instanceName}'", nameof(instanceName));
    }
}
