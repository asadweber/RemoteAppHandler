namespace HandelConsoleApp.Shared.Protocol;

public sealed record AgentResponse
{
    public ResponseStatus     Status        { get; init; }
    public string             Message       { get; init; } = string.Empty;
    public bool               IsRunning     { get; init; }
    public int?               ProcessId     { get; init; }
    public DateTime           Timestamp     { get; init; } = DateTime.UtcNow;
    public Guid               CorrelationId { get; init; }
    public List<InstanceInfo> Instances     { get; init; } = [];
}
