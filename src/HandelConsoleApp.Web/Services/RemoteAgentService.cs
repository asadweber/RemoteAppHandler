using System.Net.Sockets;
using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Web.Services;

public sealed class RemoteAgentService : IRemoteAgentService, IAsyncDisposable
{
    private readonly RemoteAgentOptions _options;
    private readonly ILogger<RemoteAgentService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TcpClient?     _tcpClient;
    private NetworkStream? _stream;
    private volatile bool  _isConnected;

    public bool IsConnected => _isConnected;

    public RemoteAgentService(
        IOptions<RemoteAgentOptions> options,
        ILogger<RemoteAgentService> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_isConnected && _tcpClient?.Connected == true) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_isConnected && _tcpClient?.Connected == true) return;

            _stream?.Dispose();
            _tcpClient?.Dispose();

            _tcpClient        = new TcpClient { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

            await _tcpClient.ConnectAsync(_options.Host, _options.Port, timeout.Token);
            _stream      = _tcpClient.GetStream();
            _isConnected = true;

            _logger.LogInformation("Connected to agent at {Host}:{Port}", _options.Host, _options.Port);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _logger.LogWarning(ex, "Cannot connect to remote agent at {Host}:{Port}", _options.Host, _options.Port);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AgentResponse> SendCommandAsync(AgentCommand command, CancellationToken ct = default)
    {
        // Reconnect outside the send-lock to avoid deadlock (EnsureConnectedAsync acquires the same lock).
        if (!_isConnected || _tcpClient?.Connected != true)
            await EnsureConnectedAsync(ct);

        await _lock.WaitAsync(ct);
        try
        {
            if (!_isConnected || _stream is null)
                throw new InvalidOperationException("Not connected to remote agent.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.CommandTimeoutSeconds));

            await ProtocolSerializer.WriteMessageAsync(_stream, command, timeout.Token);
            var response = await ProtocolSerializer.ReadMessageAsync<AgentResponse>(_stream, timeout.Token);

            return response ?? throw new InvalidOperationException("Null response from agent.");
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _logger.LogError(ex, "Error communicating with remote agent");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
