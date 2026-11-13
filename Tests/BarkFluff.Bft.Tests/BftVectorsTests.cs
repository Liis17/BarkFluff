using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text.Json;
using BarkFluff.Bft;
using Xunit;

namespace BarkFluff.Bft.Tests;

public sealed class BftVectorsTests
{
    private static JsonDocument Vectors => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "bftv1-vectors.json")));

    [Fact]
    public void ConnectionString_Vectors()
    {
        using var json = Vectors;
        var vector = json.RootElement.GetProperty("connectionString");
        var parsed = BftConnectionString.Parse(vector.GetProperty("string").GetString()!);
        Assert.Equal(Bytes(vector.GetProperty("staticPublicKey")), parsed.StaticPublicKey);
        Assert.Equal(Bytes(vector.GetProperty("obfsKey")), parsed.ObfsKey);
        Assert.Equal(vector.GetProperty("host").GetString(), parsed.Host);
        Assert.Equal(vector.GetProperty("port").GetInt32(), parsed.Port);
        Assert.Equal(vector.GetProperty("token").GetString(), parsed.Token);
        Assert.Equal(vector.GetProperty("string").GetString(), BftConnectionString.Format(parsed));

        foreach (var invalid in vector.GetProperty("invalid").EnumerateArray())
            Assert.Throws<FormatException>(() => BftConnectionString.Parse(invalid.GetString()!));
    }

    [Fact]
    public void ObfuscationKeys_And_RecordVectors()
    {
        using var json = Vectors;
        var keysVector = json.RootElement.GetProperty("obfsKeys");
        using var keys = BftObfuscationKeys.Derive(Bytes(keysVector.GetProperty("obfsKey")), Bytes(keysVector.GetProperty("salt")));
        Assert.Equal(Bytes(keysVector.GetProperty("c2sLen")), keys.ClientToServerLength.ToArray());
        Assert.Equal(Bytes(keysVector.GetProperty("c2sData")), keys.ClientToServerData.ToArray());
        Assert.Equal(Bytes(keysVector.GetProperty("s2cLen")), keys.ServerToClientLength.ToArray());
        Assert.Equal(Bytes(keysVector.GetProperty("s2cData")), keys.ServerToClientData.ToArray());

        foreach (var vector in json.RootElement.GetProperty("records").EnumerateArray())
        {
            var c2s = vector.GetProperty("keys").GetString() == "c2s";
            var expected = Bytes(vector.GetProperty("record"));
            var actual = BftObfuscatedRecords.Encrypt(keys, c2s, vector.GetProperty("n").GetUInt64(),
                Bytes(vector.GetProperty("payload")), vector.GetProperty("padLen").GetInt32());
            Assert.Equal(expected, actual);
            Assert.Equal(Bytes(vector.GetProperty("payload")), BftObfuscatedRecords.Decrypt(keys, c2s, vector.GetProperty("n").GetUInt64(), actual));
        }
    }

    [Fact]
    public async Task RecordReader_RejectsEveryNegativeRecordVector()
    {
        using var json = Vectors;
        var keyVector = json.RootElement.GetProperty("obfsKeys");
        using var keys = BftObfuscationKeys.Derive(Bytes(keyVector.GetProperty("obfsKey")), Bytes(keyVector.GetProperty("salt")));
        foreach (var vector in json.RootElement.GetProperty("negative").GetProperty("records").EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString();
            var bytes = Bytes(vector.GetProperty("stream"));
            if (name is "replay" or "reorder")
            {
                await using var stream = new ChunkedReadStream(bytes, 1);
                var reader = new BftObfuscatedRecordReader(stream, keys, clientToServer: true);
                if (name == "replay") Assert.Equal("first"u8.ToArray(), await reader.ReadAsync());
                await Assert.ThrowsAsync<BftProtocolException>(async () => await reader.ReadAsync());
            }
            else
            {
                Assert.Throws<BftProtocolException>(() => BftObfuscatedRecords.Decrypt(keys, true, 0, bytes));
            }
        }
    }

    [Fact]
    public async Task RecordReader_HandlesOneByteReadsCoalescedRecordsAndPipeReader()
    {
        using var json = Vectors;
        var keyVector = json.RootElement.GetProperty("obfsKeys");
        using var keys = BftObfuscationKeys.Derive(Bytes(keyVector.GetProperty("obfsKey")), Bytes(keyVector.GetProperty("salt")));
        var records = json.RootElement.GetProperty("records").EnumerateArray().Take(2).ToArray();
        var first = Bytes(records[0].GetProperty("record"));
        var second = Bytes(records[1].GetProperty("record"));
        var combined = first.Concat(second).ToArray();

        await using (var stream = new ChunkedReadStream(combined, 1))
        {
            var reader = new BftObfuscatedRecordReader(stream, keys, true);
            Assert.Equal(Bytes(records[0].GetProperty("payload")), await reader.ReadAsync());
            Assert.Equal(Bytes(records[1].GetProperty("payload")), await reader.ReadAsync());
            Assert.Null(await reader.ReadAsync());
        }

        await using (var truncated = new ChunkedReadStream(first[..^1], 1))
        {
            var reader = new BftObfuscatedRecordReader(truncated, keys, true);
            await Assert.ThrowsAsync<BftProtocolException>(async () => await reader.ReadAsync());
        }

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(combined);
        await pipe.Writer.CompleteAsync();
        var pipeReader = new BftObfuscatedRecordReader(pipe.Reader, keys, true);
        Assert.Equal(Bytes(records[0].GetProperty("payload")), await pipeReader.ReadAsync());
        Assert.Equal(Bytes(records[1].GetProperty("payload")), await pipeReader.ReadAsync());
        Assert.Null(await pipeReader.ReadAsync());
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public void ClientFlight_And_FullHandshakeVectors()
    {
        using var json = Vectors;
        var root = json.RootElement;
        var handshake = root.GetProperty("handshake");
        var salt = Bytes(root.GetProperty("obfsKeys").GetProperty("salt"));
        var options = ClientHandshakeOptions(handshake, root.GetProperty("obfsKeys").GetProperty("obfsKey"), salt);
        var flight = BftClientHandshake.CreateFlight(options);
        var expectedFlight = Bytes(handshake.GetProperty("clientFlight"));
        Assert.Equal(expectedFlight.AsSpan(32).ToArray(), flight.Record);
        using var keys = BftObfuscationKeys.Derive(options.ObfsKey, salt);
        var payload = BftObfuscatedRecords.Decrypt(keys, true, 0, expectedFlight.AsSpan(32));
        Assert.Equal(payload.AsSpan(0, 10).ToArray(), flight.Header);
        Assert.Equal(payload.AsSpan(10).ToArray(), flight.NoiseMessage);
        Assert.Equal(Bytes(handshake.GetProperty("exitStaticPublic")), BftCrypto.GetX25519PublicKey(Bytes(handshake.GetProperty("exitStaticPrivate"))));
    }

    [Fact]
    public async Task ClientAndServerHandshake_MatchWireVectorsAndTransport()
    {
        using var json = Vectors;
        var root = json.RootElement;
        var handshake = root.GetProperty("handshake");
        var keyVector = root.GetProperty("obfsKeys");
        var flight = Bytes(handshake.GetProperty("clientFlight"));
        var fixedTime = new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(handshake.GetProperty("timestamp").GetInt64()));
        var (clientUnderlying, serverUnderlying) = TestDuplexStream.CreatePair();
        var clientStream = new RecordingStream(clientUnderlying);
        var serverStream = new RecordingStream(serverUnderlying);
        var random = new RepeatingRandomSource([7, 0, 0, 0]);
        var serverOptions = new BftServerHandshakeOptions
        {
            StaticPrivateKey = Bytes(handshake.GetProperty("exitStaticPrivate")),
            ObfsKey = Bytes(keyVector.GetProperty("obfsKey")),
            TimeProvider = fixedTime,
            RandomSource = random,
            EphemeralPrivateKeyFactory = () => Bytes(handshake.GetProperty("serverEphemeralPrivate")),
            SaltCache = new BftSaltReplayCache(timeProvider: fixedTime)
        };
        var clientOptions = ClientHandshakeOptions(handshake, keyVector.GetProperty("obfsKey"), Bytes(keyVector.GetProperty("salt")), fixedTime);

        var serverTask = BftServerHandshake.TryAcceptAsync(serverStream, serverOptions).AsTask();
        var clientTask = BftClientHandshake.ConnectAsync(clientStream, clientOptions).AsTask();
        await Task.WhenAll(serverTask, clientTask);
        var serverResult = await serverTask;
        Assert.True(serverResult.Accepted, serverResult.Rejection?.ToString());
        await using var server = serverResult.Session!;
        await using var client = await clientTask;
        Assert.Equal(Bytes(handshake.GetProperty("handshakeHash")), client.HandshakeHash);
        Assert.Equal(client.HandshakeHash, server.HandshakeHash);
        Assert.Equal(Bytes(handshake.GetProperty("serverFirstRecord")), serverStream.WrittenBytes);

        var transport = root.GetProperty("transport").EnumerateArray().ToArray();
        var c2sAuth = Bytes(transport[0].GetProperty("plaintext"));
        var c2sOffset = clientStream.WrittenBytes.Length;
        await client.WriteMessageAsync(c2sAuth, 0);
        Assert.Equal(Bytes(transport[0].GetProperty("record")), clientStream.WrittenBytes.AsSpan(c2sOffset).ToArray());
        var s2cHello = Bytes(transport[1].GetProperty("plaintext"));
        var s2cOffset = serverStream.WrittenBytes.Length;
        await server.WriteMessageAsync(s2cHello, 3);
        Assert.Equal(Bytes(transport[1].GetProperty("record")), serverStream.WrittenBytes.AsSpan(s2cOffset).ToArray());
    }

    [Fact]
    public async Task ServerHandshake_RejectsEveryNegativeVectorWithoutClosingTheStream()
    {
        using var json = Vectors;
        var root = json.RootElement;
        var handshake = root.GetProperty("handshake");
        foreach (var vector in root.GetProperty("negative").GetProperty("handshake").EnumerateArray())
        {
            var timestamp = vector.GetProperty("now").GetInt64();
            var clock = new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(timestamp));
            var cache = new BftSaltReplayCache(timeProvider: clock);
            if (vector.TryGetProperty("precondition", out _))
                Assert.Equal(BftSaltReplayAddResult.Added, cache.TryAdd(Bytes(root.GetProperty("obfsKeys").GetProperty("salt"))));
            var (client, server) = TestDuplexStream.CreatePair();
            await using var clientScope = client;
            await using var serverScope = server;
            await client.WriteAsync(Bytes(vector.GetProperty("flight")));
            var result = await BftServerHandshake.TryAcceptAsync(server, new BftServerHandshakeOptions
            {
                StaticPrivateKey = Bytes(handshake.GetProperty("exitStaticPrivate")),
                ObfsKey = Bytes(root.GetProperty("obfsKeys").GetProperty("obfsKey")),
                TimeProvider = clock,
                SaltCache = cache,
                EphemeralPrivateKeyFactory = () => Bytes(handshake.GetProperty("serverEphemeralPrivate"))
            });
            Assert.False(result.Accepted);
            Assert.NotNull(result.Rejection);
            Assert.True(client.CanWrite);
        }
    }

    [Fact]
    public async Task ServerHandshakeTimesOutBeforeFirstRecordWithoutClosingTransport()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (client, server) = TestDuplexStream.CreatePair();
        await using var clientScope = client;
        await using var serverScope = server;
        var accepting = BftServerHandshake.TryAcceptAsync(server, new BftServerHandshakeOptions
        {
            StaticPrivateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            ObfsKey = Enumerable.Range(0x20, 32).Select(value => (byte)value).ToArray(),
            TimeProvider = clock
        }).AsTask();

        await clock.WaitForActiveTimerAsync(TimeSpan.FromSeconds(10), timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(10));
        var result = await accepting.WaitAsync(timeout.Token);
        Assert.Equal(BftServerHandshakeRejection.FirstRecordTimeout, result.Rejection);
        Assert.True(client.CanWrite);
    }

    [Fact]
    public void NoiseTransport_RekeyVectors()
    {
        using var json = Vectors;
        var root = json.RootElement;
        var handshake = root.GetProperty("handshake");
        var vectors = root.GetProperty("transport").EnumerateArray();
        foreach (var vector in vectors)
        {
            var direction = vector.GetProperty("direction").GetString();
            var noiseN = vector.GetProperty("noiseN").GetUInt64();
            var key = direction == "c2s" && noiseN == 65536
                ? Bytes(handshake.GetProperty("c2sKeyAfterRekey"))
                : Bytes(handshake.GetProperty(direction == "c2s" ? "c2sKey" : "s2cKey"));
            using var cipher = new BftNoiseCipherState(key, noiseN);
            var encrypted = cipher.Encrypt(Bytes(vector.GetProperty("plaintext")));
            using var obfs = BftObfuscationKeys.Derive(Bytes(root.GetProperty("obfsKeys").GetProperty("obfsKey")), Bytes(root.GetProperty("obfsKeys").GetProperty("salt")));
            var record = BftObfuscatedRecords.Encrypt(obfs, direction == "c2s", vector.GetProperty("recordN").GetUInt64(), encrypted, vector.GetProperty("padLen").GetInt32());
            Assert.Equal(Bytes(vector.GetProperty("record")), record);
        }
    }

    [Fact]
    public void Frames_And_NegativeVectors()
    {
        using var json = Vectors;
        foreach (var frame in json.RootElement.GetProperty("frames").EnumerateObject())
        {
            var bytes = Bytes(frame.Value);
            Assert.Equal(bytes, BftFrameCodec.Encode(BftFrameCodec.DecodeSingle(bytes)));
        }
        foreach (var invalid in json.RootElement.GetProperty("negative").GetProperty("frames").EnumerateArray())
            Assert.Throws<BftProtocolException>(() => BftFrameCodec.Decode(Bytes(invalid.GetProperty("bytes"))));
    }

    [Fact]
    public void RelayPreambleVectors()
    {
        using var json = Vectors;
        var relay = json.RootElement.GetProperty("relayPreamble");
        var key = Bytes(relay.GetProperty("relayKey"));
        foreach (var item in relay.GetProperty("valid").EnumerateArray())
        {
            var generated = BftRelayPreamble.Create(key, Bytes(relay.GetProperty("salt")), Bytes(item.GetProperty("relayNonce")),
                System.Net.IPAddress.Parse(item.GetProperty("clientIp").GetString()!), item.GetProperty("clientPort").GetInt32(),
                item.GetProperty("timestamp").GetInt64(), item.GetProperty("relayId").GetString()!);
            Assert.Equal(Bytes(item.GetProperty("preamble")), generated);
            var parsed = BftRelayPreamble.Verify(key, Bytes(relay.GetProperty("salt")), generated, item.GetProperty("timestamp").GetInt64());
            Assert.Equal(item.GetProperty("clientPort").GetInt32(), parsed.ClientPort);
            Assert.Equal(item.GetProperty("timestamp").GetInt64(), parsed.TimestampUnixSeconds);
            Assert.Equal(item.GetProperty("relayId").GetString(), parsed.RelayId);
            Assert.Equal(Bytes(item.GetProperty("relayNonce")), parsed.RelayNonce);
        }
        foreach (var invalid in json.RootElement.GetProperty("negative").GetProperty("relayPreamble").EnumerateArray())
            Assert.Throws<BftProtocolException>(() => BftRelayPreamble.Verify(key, Bytes(invalid.GetProperty("salt")), Bytes(invalid.GetProperty("preamble")), invalid.GetProperty("now").GetInt64()));
    }

    [Fact]
    public async Task FrameAndRecordFuzzing_OnlyReturnsProtocolErrors()
    {
        var random = new Random(184731);
        using var json = Vectors;
        var keyVector = json.RootElement.GetProperty("obfsKeys");
        using var keys = BftObfuscationKeys.Derive(Bytes(keyVector.GetProperty("obfsKey")), Bytes(keyVector.GetProperty("salt")));
        for (var i = 0; i < 2000; i++)
        {
            var bytes = new byte[random.Next(0, 18000)];
            random.NextBytes(bytes);
            try { _ = BftFrameCodec.Decode(bytes); }
            catch (BftProtocolException) { }
            try { _ = BftObfuscatedRecords.Decrypt(keys, true, (ulong)(i % 7), bytes); }
            catch (BftProtocolException) { }
            await using var stream = new MemoryStream(bytes, writable: false);
            var reader = new BftObfuscatedRecordReader(stream, keys, true);
            try { _ = await reader.ReadAsync(); }
            catch (BftProtocolException) { }
        }
    }

    private static BftClientHandshakeOptions ClientHandshakeOptions(JsonElement handshake, JsonElement obfsKey, byte[] salt, TimeProvider? timeProvider = null) => new()
    {
        StaticPublicKey = Bytes(handshake.GetProperty("exitStaticPublic")),
        ObfsKey = Bytes(obfsKey),
        Salt = salt,
        TimeProvider = timeProvider ?? new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(handshake.GetProperty("timestamp").GetInt64())),
        EphemeralPrivateKeyFactory = () => Bytes(handshake.GetProperty("clientEphemeralPrivate")),
        TimestampUnixSeconds = handshake.GetProperty("timestamp").GetInt64(),
        FirstRecordPaddingLength = handshake.GetProperty("clientPadLen").GetInt32()
    };

    private static byte[] Bytes(JsonElement element) => Convert.FromHexString(element.GetString()!);
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class RepeatingRandomSource(byte[] pattern) : IBftRandomSource
{
    public void Fill(Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++) destination[i] = pattern[i % pattern.Length];
    }
}

internal sealed class ChunkedReadStream(byte[] bytes, int chunkSize) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
}
