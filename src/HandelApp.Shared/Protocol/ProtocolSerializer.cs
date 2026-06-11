using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HandelApp.Shared.Protocol;

/// <summary>
/// Provides async serialization and deserialization of strongly-typed messages over a
/// <see cref="Stream"/> using a length-prefixed JSON wire format.
/// </summary>
/// <remarks>
/// Wire format: <c>[4-byte big-endian message length][UTF-8 JSON body]</c>.
/// Big-endian byte order follows network byte order convention (RFC 1700), making the
/// protocol compatible with non-.NET implementations.
/// <para>
/// A 64 KB maximum message size (<see cref="MaxMessageBytes"/>) guards against
/// out-of-memory attacks from malformed or malicious length headers.
/// </para>
/// <para>
/// JSON is serialised with <see cref="JsonNamingPolicy.CamelCase"/> and
/// <see cref="JsonStringEnumConverter"/> so enum values travel as strings (e.g. "start")
/// rather than integers, improving debuggability of raw TCP traffic.
/// </para>
/// </remarks>
public static class ProtocolSerializer
{
    /// <summary>Maximum allowed message body size in bytes. Prevents allocation of oversized buffers.</summary>
    private const int MaxMessageBytes = 65_536; // 64 KB guard

    /// <summary>
    /// Shared serializer options used for both read and write operations to guarantee
    /// symmetric encoding/decoding.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Wire format: [4-byte big-endian length][UTF-8 JSON body]
    /// <summary>
    /// Serialises <paramref name="message"/> to JSON, prepends a 4-byte big-endian length
    /// header, and writes both to <paramref name="stream"/>.
    /// </summary>
    /// <typeparam name="T">The message type to serialise.</typeparam>
    /// <param name="stream">Writable stream (typically a <see cref="System.Net.Sockets.NetworkStream"/>).</param>
    /// <param name="message">The message object to serialise and send.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task WriteMessageAsync<T>(
        Stream stream, T message, CancellationToken ct = default)
    {
        var json   = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, json.Length);

        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(json, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// Reads a length-prefixed message from <paramref name="stream"/> and deserialises it to
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <param name="stream">Readable stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The deserialised message, or <see langword="default"/> when the stream returns zero bytes
    /// (clean disconnect).
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the length header is zero, negative, or exceeds <see cref="MaxMessageBytes"/>.
    /// </exception>
    public static async Task<T?> ReadMessageAsync<T>(
        Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct))
            return default;

        var length = BinaryPrimitives.ReadInt32BigEndian(header);

        // Reject implausible lengths before allocating the body buffer.
        if (length <= 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Invalid message length: {length}");

        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct))
            return default;

        return JsonSerializer.Deserialize<T>(body, Options);
    }

    /// <summary>
    /// Reads exactly <c>buffer.Length</c> bytes from <paramref name="stream"/>, looping
    /// as necessary because a single <see cref="Stream.ReadAsync"/> may return fewer bytes
    /// than requested (common with TCP streams).
    /// </summary>
    /// <param name="stream">Readable stream.</param>
    /// <param name="buffer">Buffer to fill completely.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the buffer was filled;
    /// <see langword="false"/> on a clean disconnect (zero-byte read before buffer is full).
    /// </returns>
    private static async Task<bool> ReadExactAsync(
        Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) return false; // clean disconnect
            offset += read;
        }
        return true;
    }
}
