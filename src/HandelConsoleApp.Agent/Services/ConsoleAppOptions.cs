namespace HandelConsoleApp.Agent.Services;

public sealed class ConsoleAppOptions
{
    public string ExecutablePath        { get; set; } = string.Empty;
    public string WorkingDirectory      { get; set; } = string.Empty;
    public string Arguments             { get; set; } = string.Empty;
    public int    ShutdownGracePeriodMs { get; set; } = 10_000;
}
