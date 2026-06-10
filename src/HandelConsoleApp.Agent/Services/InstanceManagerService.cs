using HandelConsoleApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelConsoleApp.Agent.Services;

public sealed class InstanceManagerService(
    IOptions<ConsoleAppOptions> options,
    ProcessManagerRegistry registry,
    ILogger<InstanceManagerService> logger)
{
    private readonly ConsoleAppOptions _opts = options.Value;

    public AgentResponse CreateInstance(int number, string requestedBy)
    {
        if (number <= 0)
            return Error("Instance number must be greater than zero");

        var instanceName = $"{_opts.InstanceNamePrefix}-{number}";
        var targetPath   = Path.Combine(_opts.InstancesRootPath, instanceName);

        if (!IsContained(targetPath, _opts.InstancesRootPath, out var reason))
            return Error(reason);

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

    public AgentResponse ListInstances()
    {
        var managers   = registry.GetAll();
        var instances  = new List<InstanceInfo>();

        // Default instance first (if folder exists)
        if (!string.IsNullOrEmpty(_opts.DefaultInstancePath) && Directory.Exists(_opts.DefaultInstancePath))
        {
            managers.TryGetValue(_opts.DefaultInstanceName, out var defMgr);
            instances.Add(new InstanceInfo
            {
                InstanceName = _opts.DefaultInstanceName,
                FolderPath   = _opts.DefaultInstancePath,
                IsRunning    = defMgr?.IsRunning ?? false,
                ProcessId    = defMgr?.ProcessId,
                IsDefault    = true
            });
        }

        // Numbered instances
        if (Directory.Exists(_opts.InstancesRootPath))
        {
            var pattern = $"{_opts.InstanceNamePrefix}-*";
            var folders = Directory.GetDirectories(_opts.InstancesRootPath, pattern);

            foreach (var folder in folders.OrderBy(f => f))
            {
                var name = Path.GetFileName(folder);
                managers.TryGetValue(name, out var m);
                instances.Add(new InstanceInfo
                {
                    InstanceName = name,
                    FolderPath   = folder,
                    IsRunning    = m?.IsRunning ?? false,
                    ProcessId    = m?.ProcessId,
                    IsDefault    = false
                });
            }
        }

        return new AgentResponse { Status = ResponseStatus.Ok, Message = $"{instances.Count} instance(s) found", Instances = instances };
    }

    // Ensures resolved path stays inside root — blocks traversal via "..", symlinks, etc.
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

    private static AgentResponse Error(string message) =>
        new() { Status = ResponseStatus.Error, Message = message };

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var subDir in Directory.GetDirectories(source))
            CopyDirectory(subDir, Path.Combine(destination, Path.GetFileName(subDir)));
    }
}
