namespace HandelApp.Shared.Protocol;

/// <summary>
/// Response returned by the agent to the web application after processing an <see cref="AgentCommand"/>.
/// Serialised as length-prefixed JSON by <see cref="ProtocolSerializer"/>.
/// </summary>
/// <remarks>
/// Properties not relevant to the current command are left at their default values
/// (empty lists, <see langword="false"/>, <see langword="null"/>).
/// </remarks>
public sealed record AgentResponse
{
    /// <summary>High-level outcome of the command. Always present.</summary>
    public ResponseStatus      Status        { get; init; }

    /// <summary>
    /// Human-readable description of the outcome, error detail, or status message.
    /// Never <see langword="null"/>; may be empty for simple success responses.
    /// </summary>
    public string              Message       { get; init; } = string.Empty;

    /// <summary>
    /// Whether the relevant process instance is currently running.
    /// Meaningful for Start, Stop, Status, and ListInstances responses.
    /// </summary>
    public bool                IsRunning     { get; init; }

    /// <summary>
    /// OS process ID of the running instance, or <see langword="null"/> when not running.
    /// </summary>
    public int?                ProcessId     { get; init; }

    /// <summary>
    /// UTC timestamp of when the response was created on the agent side.
    /// Useful for diagnosing clock skew or stale status readings.
    /// </summary>
    public DateTime            Timestamp     { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Echo of <see cref="AgentCommand.CorrelationId"/> from the originating command.
    /// Allows the web client to match responses to requests on a persistent connection.
    /// </summary>
    public Guid                CorrelationId { get; init; }

    /// <summary>
    /// Instance list returned by <see cref="CommandType.ListInstances"/>.
    /// Empty for all other command types.
    /// </summary>
    public List<InstanceInfo>  Instances     { get; init; } = [];

    /// <summary>
    /// App definition list returned by <see cref="CommandType.ListApps"/>.
    /// Empty for all other command types.
    /// </summary>
    public List<AppDefinition> Apps          { get; init; } = [];
}
