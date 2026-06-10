namespace HandelConsoleApp.Shared.Protocol;

public sealed record AgentCommand
{
    public CommandType Command       { get; init; }
    public string      RequestedBy   { get; init; } = string.Empty;
    public Guid        CorrelationId { get; init; } = Guid.NewGuid();
}
