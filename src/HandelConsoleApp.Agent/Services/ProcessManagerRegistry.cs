using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace HandelConsoleApp.Agent.Services;

public sealed class ProcessManagerRegistry(
    IOptions<ConsoleAppOptions> options,
    ILoggerFactory loggerFactory)
{
    private readonly ConsoleAppOptions _opts = options.Value;
    private readonly Dictionary<string, ProcessManagerService> _managers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ProcessManagerService GetOrCreate(string instanceName)
    {
        bool isDefault = string.Equals(instanceName, _opts.DefaultInstanceName, StringComparison.OrdinalIgnoreCase);

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

            var instanceOpts = new ConsoleAppOptions
            {
                ExecutablePath        = exePath,
                WorkingDirectory      = instancePath,
                Arguments             = _opts.Arguments,
                ShutdownGracePeriodMs = _opts.ShutdownGracePeriodMs
            };

            var manager = new ProcessManagerService(
                new OptionsWrapper<ConsoleAppOptions>(instanceOpts),
                loggerFactory.CreateLogger<ProcessManagerService>());

            _managers[instanceName] = manager;
            return manager;
        }
    }

    public IReadOnlyDictionary<string, ProcessManagerService> GetAll()
    {
        lock (_lock) { return new Dictionary<string, ProcessManagerService>(_managers); }
    }

    // Strict allowlist: only "Prefix-N" where N is one or more digits
    private void ValidateInstanceName(string instanceName)
    {
        var pattern = $@"^{Regex.Escape(_opts.InstanceNamePrefix)}-\d+$";
        if (!Regex.IsMatch(instanceName, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException($"Invalid instance name: '{instanceName}'", nameof(instanceName));
    }
}
