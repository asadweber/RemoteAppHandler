using System.Net.Sockets;
using HandelApp.Shared.Protocol;
using Microsoft.Extensions.Options;

namespace HandelApp.Web.Services;

/// <summary>
/// Singleton service that maintains a persistent TCP connection to the remote agent process
/// and sends <see cref="AgentCommand"/> messages, returning their <see cref="AgentResponse"/>.
/// </summary>
/// <remarks>
/// Concurrency: a <see cref="SemaphoreSlim"/> with maximum count 1 serialises both connection
/// establishment and command sends, preventing interleaved frames on the shared stream.
/// <para>
/// Reconnection: connection state is tracked in <see cref="_isConnected"/> (volatile for
/// fast reads without the semaphore). <see cref="AgentConnectionMonitor"/> calls
/// <see cref="EnsureConnectedAsync"/> on a configurable interval to restore dropped connections
/// before the next command arrives.
/// </para>
/// <para>
/// Disposal: implements <see cref="IAsyncDisposable"/> because <see cref="NetworkStream"/>
/// supports async disposal. The semaphore must be disposed after the stream to avoid
/// disposing a lock that is still held.
/// </para>
/// </remarks>
public sealed class RemoteAgentService : IRemoteAgentService, IAsyncDisposable
{
    private readonly RemoteAgentOptions _options;
    private readonly ILogger<RemoteAgentService> _logger;

    /// <summary>
    /// Serialises connect and send operations so only one thread manipulates the TCP stream at a time.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TcpClient?     _tcpClient;
    private NetworkStream? _stream;

    /// <summary>
    /// Volatile because <see cref="IsConnected"/> reads it without acquiring the semaphore —
    /// a stale read is acceptable for a status flag; accuracy is restored before any command send.
    /// </summary>
    private volatile bool  _isConnected;

    /// <inheritdoc/>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Initialises the service with connection options. Does not connect immediately —
    /// connection is established lazily on first use or by <see cref="AgentConnectionMonitor"/>.
    /// </summary>
    public RemoteAgentService(
        IOptions<RemoteAgentOptions> options,
        ILogger<RemoteAgentService> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    /// <summary>
    /// Ensures the TCP connection to the agent is open, (re-)connecting if necessary.
    /// Double-checked locking pattern: fast path skips the semaphore when already connected.
    /// </summary>
    /// <param name="ct">Cancellation token; also applied to the connect timeout.</param>
    /// <exception cref="Exception">Re-throws any connection exception after logging.</exception>
    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_isConnected && _tcpClient?.Connected == true) return;

        await _lock.WaitAsync(ct);
        try
        {
            // Second check inside the lock — another thread may have connected while we waited.
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

    /// <summary>
    /// Sends an <see cref="AgentCommand"/> to the agent and awaits the corresponding
    /// <see cref="AgentResponse"/>. Reconnects if not currently connected before acquiring
    /// the send-lock (to avoid deadlock with <see cref="EnsureConnectedAsync"/>).
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="ct">Cancellation token; also applied to the per-command timeout.</param>
    /// <returns>The agent's response.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the response is <see langword="null"/> (protocol violation).
    /// </exception>
    /// <exception cref="Exception">
    /// Any I/O or timeout exception is re-thrown after marking the connection as lost.
    /// </exception>
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
            // Mark disconnected so the next call attempts reconnection.
            _isConnected = false;
            _logger.LogError(ex, "Error communicating with remote agent");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
