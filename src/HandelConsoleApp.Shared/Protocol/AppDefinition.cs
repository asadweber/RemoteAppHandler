namespace HandelApp.Shared.Protocol;

public sealed record AppDefinition
{
    public string AppId               { get; init; } = string.Empty;
    public string DisplayName         { get; init; } = string.Empty;
    public string DefaultInstancePath { get; init; } = string.Empty;
    public string InstancesRootPath   { get; init; } = string.Empty;
    public string ExecutableName      { get; init; } = string.Empty;
    public string InstanceNamePrefix  { get; init; } = "Instance";
}
