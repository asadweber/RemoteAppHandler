namespace HandelApp.Shared.Protocol;

/// <summary>
/// Outcome code carried by every <see cref="AgentResponse"/>, indicating whether the
/// requested operation succeeded and, if not, the general failure category.
/// </summary>
/// <remarks>
/// Explicit integer values are assigned to prevent silent protocol breakage if the enum
/// is ever reordered. Serialised as a camelCase string by <see cref="ProtocolSerializer"/>.
/// </remarks>
public enum ResponseStatus
{
    /// <summary>The operation completed successfully.</summary>
    Ok             = 0,
    /// <summary>
    /// The target instance is already running; no duplicate was started.
    /// Also reused by <see cref="InstanceManagerService"/> to indicate a folder already exists.
    /// </summary>
    AlreadyRunning = 1,
    /// <summary>The target instance is not running; a stop or delete of a non-existent folder.</summary>
    NotRunning     = 2,
    /// <summary>The operation failed; see <see cref="AgentResponse.Message"/> for details.</summary>
    Error          = 3,
    /// <summary>The caller is not authorised to perform the operation.</summary>
    Unauthorized   = 4
}
