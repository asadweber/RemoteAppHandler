using System.Net;
using System.Net.Sockets;
using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

/// <summary>
/// .NET Generic Host background service that listens on a TCP port for inbound
/// <see cref="AgentCommand"/> messages, dispatches them to <see cref="MultiAppManagerService"/>,
/// and writes <see cref="AgentResponse"/> replies back on the same connection.
/// </summary>
/// <remarks>
/// Wire protocol: length-prefixed JSON via <see cref="ProtocolSerializer"/> — each message
/// is preceded by a 4-byte big-endian length header, allowing multiple commands to be
/// multiplexed over a single persistent TCP connection.
/// <para>
/// Security: connections from IPs not in <see cref="AgentOptions.AllowedClientIps"/> are
/// rejected immediately after accept. An empty allowlist permits all IPs (open mode).
/// </para>
/// <para>
/// Concurrency: each accepted client is handled on a separate async task (fire-and-forget
/// via <c>_ = HandleClientAsync(...)</c>). Commands within a single connection are processed
/// sequentially to preserve request/response ordering.
/// </para>
/// </remarks>
public sealed class TcpCommandListener(
    MultiAppManagerService multiAppManager,
    IOptions<AgentOptions> agentOptions,
    ILogger<TcpCommandListener> logger) : BackgroundService
{
    private readonly AgentOptions _options = agentOptions.Value;

    /// <summary>
    /// Binds the TCP listener and enters the accept loop until the host requests cancellation.
    /// Each accepted client is dispatched to <see cref="HandleClientAsync"/> on its own task.
    /// </summary>
    /// <param name="stoppingToken">Signalled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_options.BindAddress), _options.ListenPort);
        using var listener = new TcpListener(endpoint);
        listener.Start(_options.MaxConcurrentConnections);

        logger.LogInformation("Agent TCP listener started on {Endpoint}", endpoint);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                // Fire-and-forget: each client runs independently; errors are logged inside HandleClientAsync.
                _ = HandleClientAsync(client, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error accepting TCP connection");
            }
        }

        listener.Stop();
        logger.LogInformation("Agent TCP listener stopped");
    }

    /// <summary>
    /// Handles the full lifecycle of a single TCP client connection: IP allowlist check,
    /// command read loop, command dispatch, and response write.
    /// </summary>
    /// <param name="client">The accepted <see cref="TcpClient"/>; ownership transferred to this method.</param>
    /// <param name="ct">Cancellation token propagated from the host shutdown signal.</param>
    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        if (client.Client.RemoteEndPoint is IPEndPoint remoteIp)
        {
            var addr = remoteIp.Address.ToString();
            // Enforce IP allowlist when configured; an empty list means unrestricted access.
            if (_options.AllowedClientIps.Length > 0 && !_options.AllowedClientIps.Contains(addr))
            {
                logger.LogWarning("Rejected connection from {IP} (not in AllowedClientIps)", addr);
                client.Dispose();
                return;
            }
        }

        logger.LogInformation("Accepted connection from {Remote}", remoteEp);

        using (client)
        {
            await using var stream = client.GetStream();
            try
            {
                // Process commands sequentially on this connection until disconnect or shutdown.
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var command = await ProtocolSerializer.ReadMessageAsync<AgentCommand>(stream, ct);
                    if (command is null) break;  // clean disconnect — client closed the connection

                    logger.LogDebug("Command {Cmd} AppId={AppId} from {Remote} by {User}",
                        command.Command, command.AppId, remoteEp, command.RequestedBy);

                    AgentResponse response;
                    try
                    {
                        // Dispatch the command to the appropriate MultiAppManagerService method.
                        // Unknown or structurally invalid commands (e.g. RegisterApp with missing AppDefinition)
                        // fall through to the default branch and return an error response.
                        response = command.Command switch
                        {
                            CommandType.ListApps =>
                                multiAppManager.ListApps(),

                            CommandType.RegisterApp when command.AppDefinition is not null =>
                                multiAppManager.RegisterApp(command.AppDefinition, command.RequestedBy),

                            CommandType.UnregisterApp =>
                                multiAppManager.UnregisterApp(command.AppId, command.RequestedBy),

                            CommandType.ListInstances =>
                                multiAppManager.ListInstances(command.AppId),

                            CommandType.CreateInstance =>
                                multiAppManager.CreateInstance(command.AppId, command.InstanceNumber ?? 1, command.RequestedBy),

                            CommandType.DeleteInstance =>
                                multiAppManager.DeleteInstance(command.AppId, command.InstanceNumber ?? 1, command.RequestedBy),

                            CommandType.Start =>
                                multiAppManager.StartInstance(command.AppId, command.InstanceName, command.RequestedBy),

                            CommandType.Stop =>
                                multiAppManager.StopInstance(command.AppId, command.InstanceName, command.RequestedBy),

                            CommandType.Status =>
                                multiAppManager.GetInstanceStatus(command.AppId, command.InstanceName),

                            _ => new AgentResponse
                            {
                                Status  = ResponseStatus.Error,
                                Message = $"Unknown or malformed command: {command.Command}"
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        // Exceptions from business logic must not crash the connection loop —
                        // translate to an error response so the client gets a reply.
                        logger.LogWarning(ex, "Command {Cmd} from {Remote} faulted: {Msg}",
                            command.Command, remoteEp, ex.Message);
                        response = new AgentResponse { Status = ResponseStatus.Error, Message = ex.Message };
                    }

                    // Echo the sender's CorrelationId so the client can match responses to requests.
                    response = response with { CorrelationId = command.CorrelationId };
                    await ProtocolSerializer.WriteMessageAsync(stream, response, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex)
            {
                // Normal TCP disconnect — log at Debug level to avoid alert fatigue.
                logger.LogDebug(ex, "Client {Remote} disconnected", remoteEp);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling client {Remote}", remoteEp);
            }
        }

        logger.LogInformation("Connection from {Remote} closed", remoteEp);
    }
}
