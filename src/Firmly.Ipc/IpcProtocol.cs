using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Firmly.Ipc;

/// <summary>
/// Wire format shared by app and service: a 4-byte little-endian length, then that many bytes of UTF-8 JSON.
/// </summary>
public static class IpcProtocol
{
    /// <summary>Upper bound on a message, so a misbehaving client can't make the service allocate freely.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    public static Task WriteRequestAsync(Stream stream, IpcRequest request, CancellationToken ct) =>
        WriteAsync(stream, request, IpcJsonContext.Default.IpcRequest, ct);

    public static Task WriteResponseAsync(Stream stream, IpcResponse response, CancellationToken ct) =>
        WriteAsync(stream, response, IpcJsonContext.Default.IpcResponse, ct);

    /// <exception cref="InvalidDataException">The message is malformed or too large.</exception>
    public static Task<IpcRequest> ReadRequestAsync(Stream stream, CancellationToken ct) =>
        ReadAsync(stream, IpcJsonContext.Default.IpcRequest, ct);

    /// <exception cref="InvalidDataException">The message is malformed or too large.</exception>
    public static Task<IpcResponse> ReadResponseAsync(Stream stream, CancellationToken ct) =>
        ReadAsync(stream, IpcJsonContext.Default.IpcResponse, ct);

    private static async Task WriteAsync<T>(Stream stream, T message, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, typeInfo);
        if (body.Length > MaxMessageBytes)
            throw new InvalidDataException($"IPC message of {body.Length} bytes exceeds the {MaxMessageBytes}-byte limit.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<T> ReadAsync<T>(Stream stream, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes)
            throw new InvalidDataException($"Invalid IPC message length {length}.");

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        try
        {
            return JsonSerializer.Deserialize(body, typeInfo) ?? throw new InvalidDataException("Empty IPC message.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed IPC message.", ex);
        }
    }
}

// Source-generated so serialization survives trimming in the published app.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
internal sealed partial class IpcJsonContext : JsonSerializerContext
{
}
