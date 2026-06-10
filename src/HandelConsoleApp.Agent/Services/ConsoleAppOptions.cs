namespace HandelApp.Agent.Services;

public sealed class ConsoleAppOptions
{
    public string ExecutablePath        { get; set; } = string.Empty;
    public string WorkingDirectory      { get; set; } = string.Empty;
    public string Arguments             { get; set; } = string.Empty;
    public int    ShutdownGracePeriodMs { get; set; } = 10_000;

    // Multi-instance support
    public string DefaultInstancePath { get; set; } = string.Empty;
    public string DefaultInstanceName { get; set; } = "Default";
    public string InstancesRootPath   { get; set; } = string.Empty;
    public string InstanceNamePrefix  { get; set; } = "Instance";
    public string ExecutableName      { get; set; } = string.Empty;
}
