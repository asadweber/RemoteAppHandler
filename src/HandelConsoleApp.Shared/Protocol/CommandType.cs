namespace HandelApp.Shared.Protocol;

/// <summary>
/// Discriminator for <see cref="AgentCommand.Command"/>, identifying the operation the
/// web application is requesting the agent to perform.
/// </summary>
/// <remarks>
/// Explicit integer values are assigned to prevent silent protocol breakage if the enum
/// is ever reordered or extended. Both sides of the TCP connection must agree on these values.
/// </remarks>
public enum CommandType
{
    /// <summary>Start the named process instance.</summary>
    Start          = 1,
    /// <summary>Stop the named process instance (graceful, then force-kill on timeout).</summary>
    Stop           = 2,
    /// <summary>Query the run-state of the named process instance.</summary>
    Status         = 3,
    /// <summary>Provision a new numbered instance by cloning the default instance directory.</summary>
    CreateInstance = 4,
    /// <summary>Remove the directory of a stopped numbered instance.</summary>
    DeleteInstance = 5,
    /// <summary>List all known instances of an app with their run-state.</summary>
    ListInstances  = 6,
    /// <summary>Register a new application definition with the agent.</summary>
    RegisterApp    = 7,
    /// <summary>Remove an application definition (requires all instances to be stopped first).</summary>
    UnregisterApp  = 8,
    /// <summary>List all registered application definitions.</summary>
    ListApps       = 9
}
