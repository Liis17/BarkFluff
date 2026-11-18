using System.Buffers.Binary;
using System.Text;

namespace BarkFluff.Bft;

public enum BftFrameType : byte
{
    Auth = 0x01,
    Hello = 0x02,
    Open = 0x03,
    Data = 0x04,
    WindowUpdate = 0x05,
    Close = 0x06,
    Reset = 0x07,
    Ping = 0x08,
    Pong = 0x09,
    GoAway = 0x0A
}

public enum BftResetCode : byte
{
    Cancel = 0,
    ServiceUnavailable = 1,
    UpstreamFailed = 2,
    FlowControl = 3,
    StreamLimit = 4,
    IdleTimeout = 5,
    Internal = 6,
    StreamClosed = 7
}

public enum BftGoAwayCode : byte
{
    Shutdown = 0,
    AuthFailed = 1,
    ProtocolError = 2,
    Overload = 3
}

public sealed record BftHello(string ProxyId, int InitialWindow, ushort MaxStreams, byte[] TlsSpkiSha256, IReadOnlyList<byte> ServiceIds)
{
    public byte[] ToBody()
    {
        BftFrameCodec.ValidateHello(this);
        var id = Encoding.ASCII.GetBytes(ProxyId);
        var body = new byte[40 + id.Length + ServiceIds.Count];
        body[0] = (byte)id.Length;
        id.CopyTo(body, 1);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(1 + id.Length), (uint)InitialWindow);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(5 + id.Length), MaxStreams);
        TlsSpkiSha256.CopyTo(body, 7 + id.Length);
        body[39 + id.Length] = (byte)ServiceIds.Count;
        for (var i = 0; i < ServiceIds.Count; i++) body[40 + id.Length + i] = ServiceIds[i];
        return body;
    }
}

public sealed record BftFrame(BftFrameType Type, uint StreamId, byte[] Body)
{
    public static BftFrame Auth(string token) => BftFrameCodec.CreateAuth(token);
    public static BftFrame HelloFrame(BftHello hello) => new(BftFrameType.Hello, 0, hello.ToBody());
    public static BftFrame Open(uint streamId, byte serviceId) => new(BftFrameType.Open, streamId, [serviceId]);
    public static BftFrame Data(uint streamId, ReadOnlySpan<byte> data) => new(BftFrameType.Data, streamId, data.ToArray());
    public static BftFrame WindowUpdate(uint streamId, uint increment)
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(body, increment);
        return new(BftFrameType.WindowUpdate, streamId, body);
    }
    public static BftFrame Close(uint streamId) => new(BftFrameType.Close, streamId, []);
    public static BftFrame Reset(uint streamId, BftResetCode code) => new(BftFrameType.Reset, streamId, [(byte)code]);
    public static BftFrame Ping(ReadOnlySpan<byte> opaque) => new(BftFrameType.Ping, 0, opaque.ToArray());
    public static BftFrame Pong(ReadOnlySpan<byte> opaque) => new(BftFrameType.Pong, 0, opaque.ToArray());
    public static BftFrame GoAway(uint lastStreamId, BftGoAwayCode code)
    {
        var body = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(body, lastStreamId);
        body[4] = (byte)code;
        return new(BftFrameType.GoAway, 0, body);
    }
}

public static class BftFrameCodec
{
    public const int HeaderLength = 7;
    public const int MaxBodyLength = 16377;
    public const int MaxFrameLength = 16384;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Encode(BftFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Validate(frame);
        var output = new byte[HeaderLength + frame.Body.Length];
        output[0] = (byte)frame.Type;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(1), frame.StreamId);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(5), (ushort)frame.Body.Length);
        frame.Body.CopyTo(output, HeaderLength);
        return output;
    }

    public static byte[] EncodeMany(IEnumerable<BftFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        using var stream = new MemoryStream();
        foreach (var frame in frames)
        {
            var encoded = Encode(frame);
            if (stream.Length + encoded.Length > 16384)
                throw new ArgumentException("Frames do not fit in one Noise message.", nameof(frames));
            stream.Write(encoded);
        }
        return stream.ToArray();
    }

    public static IReadOnlyList<BftFrame> Decode(ReadOnlySpan<byte> message)
    {
        if (message.Length > 16384) throw new BftProtocolException("Mux message exceeds 16384 bytes.");
        var frames = new List<BftFrame>();
        var offset = 0;
        while (offset < message.Length)
        {
            if (message.Length - offset < HeaderLength)
                throw new BftProtocolException("Truncated mux frame header.");
            var type = (BftFrameType)message[offset];
            var streamId = BinaryPrimitives.ReadUInt32BigEndian(message[(offset + 1)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 5)..]);
            if (length > MaxBodyLength || message.Length - offset - HeaderLength < length)
                throw new BftProtocolException("Invalid mux frame length.");
            var frame = new BftFrame(type, streamId, message.Slice(offset + HeaderLength, length).ToArray());
            Validate(frame);
            frames.Add(frame);
            offset += HeaderLength + length;
        }
        return frames;
    }

    public static BftFrame DecodeSingle(ReadOnlySpan<byte> message)
    {
        var frames = Decode(message);
        if (frames.Count != 1) throw new BftProtocolException("Expected exactly one mux frame.");
        return frames[0];
    }

    public static BftFrame CreateAuth(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(token); }
        catch (EncoderFallbackException ex) { throw new ArgumentException("AUTH token must be valid UTF-8.", nameof(token), ex); }
        if (bytes.Length is < 1 or > 256) throw new ArgumentException("AUTH token must be 1–256 UTF-8 bytes.", nameof(token));
        return new BftFrame(BftFrameType.Auth, 0, bytes);
    }

    public static BftHello ParseHello(ReadOnlySpan<byte> body)
    {
        if (body.Length < 40) throw new BftProtocolException("Truncated HELLO body.");
        var idLength = body[0];
        if (idLength is < 1 or > 32 || body.Length < 40 + idLength)
            throw new BftProtocolException("Invalid HELLO proxy ID length.");
        var idBytes = body.Slice(1, idLength);
        if (idBytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new BftProtocolException("HELLO proxy ID must be ASCII.");
        var proxyId = Encoding.ASCII.GetString(idBytes);
        var initialWindow = BinaryPrimitives.ReadUInt32BigEndian(body[(1 + idLength)..]);
        var maxStreams = BinaryPrimitives.ReadUInt16BigEndian(body[(5 + idLength)..]);
        var pin = body.Slice(7 + idLength, 32).ToArray();
        var count = body[39 + idLength];
        if (body.Length != 40 + idLength + count)
            throw new BftProtocolException("Invalid HELLO service list length.");
        var serviceIds = body.Slice(40 + idLength, count).ToArray();
        var hello = new BftHello(proxyId, initialWindow <= int.MaxValue ? (int)initialWindow : -1, maxStreams, pin, serviceIds);
        ValidateHello(hello);
        return hello;
    }

    internal static void ValidateHello(BftHello hello)
    {
        if (hello.ProxyId.Length is < 1 or > 32 || hello.ProxyId.Any(c => c > 0x7F))
            throw new BftProtocolException("HELLO proxy ID must be 1–32 ASCII bytes.");
        if (hello.InitialWindow is < 16384 or > int.MaxValue || hello.MaxStreams < 1)
            throw new BftProtocolException("HELLO limits are invalid.");
        if (hello.TlsSpkiSha256 is null || hello.TlsSpkiSha256.Length != 32)
            throw new BftProtocolException("HELLO TLS SPKI SHA-256 pin must be 32 bytes.");
        if (hello.ServiceIds.Count > byte.MaxValue)
            throw new BftProtocolException("HELLO contains too many services.");
        var seen = new HashSet<byte>();
        foreach (var serviceId in hello.ServiceIds)
        {
            if (serviceId is < 1 or > 12 || !seen.Add(serviceId))
                throw new BftProtocolException("HELLO service IDs must be known and unique.");
        }
    }

    private static void Validate(BftFrame frame)
    {
        if (frame.Body.Length > MaxBodyLength) throw new BftProtocolException("Mux frame body is too large.");
        var tunnelFrame = frame.Type is BftFrameType.Auth or BftFrameType.Hello or BftFrameType.Ping or BftFrameType.Pong or BftFrameType.GoAway;
        if (tunnelFrame ? frame.StreamId != 0 : frame.StreamId == 0)
            throw new BftProtocolException("Mux frame uses an invalid stream ID.");
        switch (frame.Type)
        {
            case BftFrameType.Auth:
                if (frame.Body.Length is < 1 or > 256) throw new BftProtocolException("Invalid AUTH length.");
                try { _ = StrictUtf8.GetString(frame.Body); }
                catch (DecoderFallbackException ex) { throw new BftProtocolException("AUTH token is not valid UTF-8.", ex); }
                break;
            case BftFrameType.Hello:
                _ = ParseHello(frame.Body);
                break;
            case BftFrameType.Open:
                if (frame.Body.Length != 1 || frame.Body[0] is < 1 or > 12) throw new BftProtocolException("Invalid OPEN service ID.");
                break;
            case BftFrameType.Data:
                if (frame.Body.Length is < 1 or > MaxBodyLength) throw new BftProtocolException("Invalid DATA length.");
                break;
            case BftFrameType.WindowUpdate:
                if (frame.Body.Length != 4 || BinaryPrimitives.ReadUInt32BigEndian(frame.Body) == 0)
                    throw new BftProtocolException("Invalid WINDOW_UPDATE increment.");
                break;
            case BftFrameType.Close:
                if (frame.Body.Length != 0) throw new BftProtocolException("CLOSE body must be empty.");
                break;
            case BftFrameType.Reset:
                if (frame.Body.Length != 1) throw new BftProtocolException("RESET body must contain one code.");
                break;
            case BftFrameType.Ping:
            case BftFrameType.Pong:
                if (frame.Body.Length != 8) throw new BftProtocolException("PING and PONG bodies must be 8 bytes.");
                break;
            case BftFrameType.GoAway:
                if (frame.Body.Length != 5) throw new BftProtocolException("GOAWAY body must be 5 bytes.");
                break;
            default:
                throw new BftProtocolException("Unknown mux frame type.");
        }
    }
}
