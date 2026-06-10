using System.Net;
using System.Net.Sockets;
using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Agent.Services;

public sealed class TcpCommandListener(
    MultiAppManagerService multiAppManager,
    IOptions<AgentOptions> agentOptions,
    ILogger<TcpCommandListener> logger) : BackgroundService
{
    private readonly AgentOptions _options = agentOptions.Value;

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

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        if (client.Client.RemoteEndPoint is IPEndPoint remoteIp)
        {
            var addr = remoteIp.Address.ToString();
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
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var command = await ProtocolSerializer.ReadMessageAsync<AgentCommand>(stream, ct);
                    if (command is null) break;

                    logger.LogDebug("Command {Cmd} AppId={AppId} from {Remote} by {User}",
                        command.Command, command.AppId, remoteEp, command.RequestedBy);

                    AgentResponse response;
                    try
                    {
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
                    catch (ArgumentException ex)
                    {
                        logger.LogWarning("Rejected command {Cmd} from {Remote}: {Msg}",
                            command.Command, remoteEp, ex.Message);
                        response = new AgentResponse { Status = ResponseStatus.Error, Message = ex.Message };
                    }

                    response = response with { CorrelationId = command.CorrelationId };
                    await ProtocolSerializer.WriteMessageAsync(stream, response, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex)
            {
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
