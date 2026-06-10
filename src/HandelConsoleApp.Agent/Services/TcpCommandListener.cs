using System.Net;
using System.Net.Sockets;
using HandelConsoleApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelConsoleApp.Agent.Services;

public sealed class TcpCommandListener(
    ProcessManagerService processManager,
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

                    logger.LogDebug("Command {Cmd} from {Remote} by {User}",
                        command.Command, remoteEp, command.RequestedBy);

                    var response = command.Command switch
                    {
                        CommandType.Start  => processManager.Start(command.RequestedBy),
                        CommandType.Stop   => processManager.Stop(command.RequestedBy),
                        CommandType.Status => processManager.GetStatus(),
                        _ => new AgentResponse
                        {
                            Status  = ResponseStatus.Error,
                            Message = $"Unknown command: {command.Command}"
                        }
                    };

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
