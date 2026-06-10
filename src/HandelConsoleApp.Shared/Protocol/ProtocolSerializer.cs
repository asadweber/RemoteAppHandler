using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HandelConsoleApp.Shared.Protocol;

public static class ProtocolSerializer
{
    private const int MaxMessageBytes = 65_536; // 64 KB guard

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Wire format: [4-byte big-endian length][UTF-8 JSON body]
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

    public static async Task<T?> ReadMessageAsync<T>(
        Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct))
            return default;

        var length = BinaryPrimitives.ReadInt32BigEndian(header);

        if (length <= 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Invalid message length: {length}");

        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct))
            return default;

        return JsonSerializer.Deserialize<T>(body, Options);
    }

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
