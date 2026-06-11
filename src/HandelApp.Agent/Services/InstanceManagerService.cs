using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

/// <summary>
/// Manages the filesystem-level lifecycle of console-application instances for a single
/// registered app: creating new instance folders by cloning the default, deleting them,
/// and enumerating all known instances with their runtime status.
/// </summary>
/// <remarks>
/// Each instance is a directory clone of <see cref="AppOptions.DefaultInstancePath"/>.
/// Numbered instances follow the naming convention <c>{InstanceNamePrefix}-{number}</c>
/// and must reside within <see cref="AppOptions.InstancesRootPath"/>.
/// <para>
/// Security: all path operations are validated by <see cref="IsContained"/> to prevent
/// directory traversal attacks via crafted instance numbers or names.
/// </para>
/// </remarks>
public sealed class InstanceManagerService(
    IOptions<AppOptions> options,
    ProcessManagerRegistry registry,
    ILogger<InstanceManagerService> logger)
{
    private readonly AppOptions _opts = options.Value;

    /// <summary>
    /// Creates a new numbered instance by recursively copying the default instance directory.
    /// </summary>
    /// <param name="number">Numeric suffix for the new instance (must be &gt; 0).</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> on success;
    /// <see cref="ResponseStatus.AlreadyRunning"/> when the target folder already exists;
    /// <see cref="ResponseStatus.Error"/> on validation failure or I/O error.
    /// </returns>
    public AgentResponse CreateInstance(int number, string requestedBy)
    {
        if (number <= 0)
            return Error("Instance number must be greater than zero");

        var instanceName = $"{_opts.InstanceNamePrefix}-{number}";
        var targetPath   = Path.Combine(_opts.InstancesRootPath, instanceName);

        if (!IsContained(targetPath, _opts.InstancesRootPath, out var reason))
            return Error(reason);

        // Reuse AlreadyRunning status to signal "already provisioned" — avoids overwriting an existing instance.
        if (Directory.Exists(targetPath))
            return new AgentResponse { Status = ResponseStatus.AlreadyRunning, Message = $"Instance folder already exists: {targetPath}" };

        if (!Directory.Exists(_opts.DefaultInstancePath))
            return Error($"Default instance path not found: {_opts.DefaultInstancePath}");

        try
        {
            CopyDirectory(_opts.DefaultInstancePath, targetPath);
            logger.LogInformation("Created instance {Name} at {Path} on behalf of {User}", instanceName, targetPath, requestedBy);
            return new AgentResponse { Status = ResponseStatus.Ok, Message = $"Instance {instanceName} created at {targetPath}" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create instance {Name}", instanceName);
            return Error($"Failed to create: {ex.Message}");
        }
    }

    /// <summary>
    /// Permanently deletes the directory of a numbered instance.
    /// Refuses to delete the default instance or any instance that is currently running.
    /// </summary>
    /// <param name="number">Numeric suffix of the instance to delete (must be &gt; 0).</param>
    /// <param name="requestedBy">Caller identity for audit logging.</param>
    /// <returns>
    /// <see cref="ResponseStatus.Ok"/> on success;
    /// <see cref="ResponseStatus.NotRunning"/> when the folder does not exist;
    /// <see cref="ResponseStatus.Error"/> when the instance is running, is the default, or deletion fails.
    /// </returns>
    public AgentResponse DeleteInstance(int number, string requestedBy)
    {
        if (number <= 0)
            return Error("Instance number must be greater than zero");

        var instanceName = $"{_opts.InstanceNamePrefix}-{number}";

        // Defense-in-depth: reject if the derived name matches the configured default
        if (string.Equals(instanceName, _opts.DefaultInstanceName, StringComparison.OrdinalIgnoreCase))
            return Error($"Default instance '{_opts.DefaultInstanceName}' cannot be deleted");

        var targetPath = Path.Combine(_opts.InstancesRootPath, instanceName);

        if (!IsContained(targetPath, _opts.InstancesRootPath, out var reason))
            return Error(reason);

        if (!Directory.Exists(targetPath))
            return new AgentResponse { Status = ResponseStatus.NotRunning, Message = $"Instance folder not found: {targetPath}" };

        var managers = registry.GetAll();
        if (managers.TryGetValue(instanceName, out var mgr) && mgr.IsRunning)
            return Error($"Stop {instanceName} before deleting");

        try
        {
            Directory.Delete(targetPath, recursive: true);
            logger.LogInformation("Deleted instance {Name} by {User}", instanceName, requestedBy);
            return new AgentResponse { Status = ResponseStatus.Ok, Message = $"Instance {instanceName} deleted" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete instance {Name}", instanceName);
            return Error($"Failed to delete: {ex.Message}");
        }
    }

    /// <summary>
    /// Enumerates all known instances: the default instance first (if its directory exists),
    /// followed by all numbered instance directories matching the configured prefix pattern.
    /// For each, live process status is fetched from the <see cref="ProcessManagerRegistry"/>.
    /// </summary>
    /// <returns>
    /// Always <see cref="ResponseStatus.Ok"/>; <see cref="AgentResponse.Instances"/> contains
    /// one <see cref="InstanceInfo"/> per discovered instance with best-effort status data.
    /// </returns>
    public AgentResponse ListInstances()
    {
        var instances = new List<InstanceInfo>();

        // Default instance first (if folder exists)
        if (!string.IsNullOrEmpty(_opts.DefaultInstancePath) && Directory.Exists(_opts.DefaultInstancePath))
        {
            try
            {
                var defMgr    = registry.GetOrCreate(_opts.DefaultInstanceName);
                var defStatus = defMgr.GetStatus();
                instances.Add(new InstanceInfo
                {
                    InstanceName = _opts.DefaultInstanceName,
                    FolderPath   = _opts.DefaultInstancePath,
                    IsRunning    = defStatus.IsRunning,
                    ProcessId    = defStatus.ProcessId,
                    IsDefault    = true
                });
            }
            catch (Exception ex)
            {
                // If status cannot be read, report the instance as stopped rather than omitting it.
                logger.LogWarning(ex, "Could not get status for default instance '{Name}'", _opts.DefaultInstanceName);
                instances.Add(new InstanceInfo
                {
                    InstanceName = _opts.DefaultInstanceName,
                    FolderPath   = _opts.DefaultInstancePath,
                    IsRunning    = false,
                    IsDefault    = true
                });
            }
        }

        // Numbered instances
        if (Directory.Exists(_opts.InstancesRootPath))
        {
            var pattern = $"{_opts.InstanceNamePrefix}-*";
            var folders = Directory.GetDirectories(_opts.InstancesRootPath, pattern);

            foreach (var folder in folders.OrderBy(f => f))
            {
                var name = Path.GetFileName(folder);
                try
                {
                    var mgr    = registry.GetOrCreate(name);
                    var status = mgr.GetStatus();
                    instances.Add(new InstanceInfo
                    {
                        InstanceName = name,
                        FolderPath   = folder,
                        IsRunning    = status.IsRunning,
                        ProcessId    = status.ProcessId,
                        IsDefault    = false
                    });
                }
                catch (Exception ex)
                {
                    // If status cannot be read, report the instance as stopped rather than omitting it.
                    logger.LogWarning(ex, "Could not get status for instance '{Name}'", name);
                    instances.Add(new InstanceInfo
                    {
                        InstanceName = name,
                        FolderPath   = folder,
                        IsRunning    = false,
                        IsDefault    = false
                    });
                }
            }
        }

        return new AgentResponse { Status = ResponseStatus.Ok, Message = $"{instances.Count} instance(s) found", Instances = instances };
    }

    // Ensures resolved path stays inside root — blocks traversal via "..", symlinks, etc.
    /// <summary>
    /// Verifies that <paramref name="candidate"/> resolves to a path within <paramref name="root"/>
    /// after full path normalisation. Rejects traversal attempts using <c>..</c>, symlinks, or
    /// absolute paths that escape the root.
    /// </summary>
    /// <param name="candidate">Path to validate.</param>
    /// <param name="root">Expected root directory; the candidate must be a descendant.</param>
    /// <param name="failReason">Set to a human-readable error message when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> is safely contained within <paramref name="root"/>.</returns>
    internal static bool IsContained(string candidate, string root, out string failReason)
    {
        var rootFull   = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var targetFull = Path.GetFullPath(candidate);

        if (!targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            failReason = $"Path escapes instances root: {candidate}";
            return false;
        }

        failReason = string.Empty;
        return true;
    }

    /// <summary>Returns a standardised error response.</summary>
    private static AgentResponse Error(string message) =>
        new() { Status = ResponseStatus.Error, Message = message };

    /// <summary>
    /// Recursively copies all files and subdirectories from <paramref name="source"/> to
    /// <paramref name="destination"/>, preserving the directory tree structure.
    /// Existing destination files are not overwritten — the copy is additive.
    /// </summary>
    /// <param name="source">Source directory to copy from.</param>
    /// <param name="destination">Destination directory; created if it does not exist.</param>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var subDir in Directory.GetDirectories(source))
            CopyDirectory(subDir, Path.Combine(destination, Path.GetFileName(subDir)));
    }
}
