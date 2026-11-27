using System.Net;
using System.Text;

namespace Integrator.Telemetry;

/// <summary>
/// Talks to the game's local gRPC server over cleartext HTTP/2 using nothing but
/// HttpClient. gRPC is just HTTP/2 POST with a length-prefixed protobuf body, so
/// frames are built and parsed by hand with no extra dependencies.
/// </summary>
internal sealed class GameClient : IDisposable
{
    private const string Service = "sc.game.telemetry.v1.GameTelemetryService";

    private readonly string _baseUrl;
    private readonly HttpClient _http;

    public GameClient(string source)
    {
        // Allow HTTP/2 without TLS (the game server is plain h2c on loopback).
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        _baseUrl = $"http://{source}/{Service}/";
        _http = new HttpClient { DefaultRequestVersion = HttpVersion.Version20 };
    }

    /// <summary>Open the stream, read the first frame, return its protobuf bytes.</summary>
    public Task<byte[]> PullFrameAsync(CancellationToken ct) =>
        GrpcCallAsync("TelemetryStream", new byte[5], ct,
            "No telemetry frame received — are you loaded into the game (not the menu)?");

    /// <summary>Call ListTriggerableActions; return the raw protobuf response bytes.</summary>
    public Task<byte[]> ListActionsRawAsync(CancellationToken ct) =>
        GrpcCallAsync("ListTriggerableActions", new byte[5], ct,
            "No response from ListTriggerableActions.");

    /// <summary>Call TriggerGameAction; return the raw protobuf response bytes.</summary>
    public Task<byte[]> TriggerActionRawAsync(string actionName, int holdMs, CancellationToken ct) =>
        GrpcCallAsync("TriggerGameAction", BuildTriggerFrame(actionName, holdMs), ct,
            "No response from TriggerGameAction.");

    private async Task<byte[]> GrpcCallAsync(string method, byte[] grpcFrame, CancellationToken ct, string eofError)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + method)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(grpcFrame),
        };
        req.Content.Headers.TryAddWithoutValidation("Content-Type", "application/grpc");
        req.Headers.TryAddWithoutValidation("TE", "trailers");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, 5, ct))
            throw new IOException(eofError);

        int len = (header[1] << 24) | (header[2] << 16) | (header[3] << 8) | header[4];
        var body = new byte[len];
        if (len > 0 && !await ReadExactAsync(stream, body, len, ct))
            throw new IOException("gRPC response frame was truncated.");
        return body;
    }

    /// <summary>
    /// Encode TriggerGameActionRequest { string action_name = 1; uint32 hold_ms = 2; }
    /// and wrap it in a gRPC length-prefix frame.
    /// </summary>
    private static byte[] BuildTriggerFrame(string actionName, int holdMs)
    {
        var proto = new List<byte>(actionName.Length + 10);

        // field 1: action_name (string, wire type 2 = length-delimited)
        byte[] nameBytes = Encoding.UTF8.GetBytes(actionName);
        proto.Add(0x0A);
        AppendVarint(proto, (ulong)nameBytes.Length);
        proto.AddRange(nameBytes);

        // field 2: hold_ms (uint32, wire type 0 = varint) — omit when 0
        if (holdMs > 0)
        {
            proto.Add(0x10);
            AppendVarint(proto, (ulong)holdMs);
        }

        // gRPC frame: 1 byte compression flag (0) + 4 bytes big-endian length + proto body
        byte[] frame = new byte[5 + proto.Count];
        frame[1] = (byte)(proto.Count >> 24);
        frame[2] = (byte)(proto.Count >> 16);
        frame[3] = (byte)(proto.Count >> 8);
        frame[4] = (byte)proto.Count;
        proto.CopyTo(frame, 5);
        return frame;
    }

    private static void AppendVarint(List<byte> buf, ulong value)
    {
        while (value > 0x7F) { buf.Add((byte)((value & 0x7F) | 0x80)); value >>= 7; }
        buf.Add((byte)value);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, int n, CancellationToken ct)
    {
        int off = 0;
        while (off < n)
        {
            int r = await s.ReadAsync(buf.AsMemory(off, n - off), ct);
            if (r == 0) return false;
            off += r;
        }
        return true;
    }

    public void Dispose() => _http.Dispose();
}
