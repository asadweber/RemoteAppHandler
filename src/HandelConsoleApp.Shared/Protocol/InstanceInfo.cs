namespace HandelApp.Shared.Protocol;

public sealed record InstanceInfo
{
    public string InstanceName { get; init; } = string.Empty;
    public string FolderPath   { get; init; } = string.Empty;
    public bool   IsRunning    { get; init; }
    public int?   ProcessId    { get; init; }
    public bool   IsDefault    { get; init; }
}
