using System.Text.Json;
using BarkFluff.Bft;
using Xunit;

namespace BarkFluff.Bft.Tests;

public sealed class BftMuxSequencesTests
{
    [Fact]
    public async Task MuxSequences_MatchAllDeclaredActions()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "bftv1-vectors.json")));
        foreach (var sequence in json.RootElement.GetProperty("muxSequences").EnumerateArray())
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var tunnel = await ConnectAsync(
                sequence.TryGetProperty("initialWindow", out var initialWindow) ? initialWindow.GetInt32() : 262144,
                cancellation.Token);
            var target = sequence.GetProperty("side").GetString() == "exit"
                ? tunnel.Server.Connection
                : tunnel.Client.Connection;
            var frames = sequence.GetProperty("frames").EnumerateArray()
                .Select(frame => BftFrameCodec.DecodeSingle(Convert.FromHexString(frame.GetString()!)))
                .ToArray();
            var expected = sequence.GetProperty("expect").GetString()!;
            var sentGoAway = sequence.TryGetProperty("sentGoawayLastStreamId", out var lastStreamId);
            var action = BftMuxFrameAction.Processed;
            var protocolError = false;

            for (var i = 0; i < frames.Length; i++)
            {
                try { action = await target.ProcessFrameAsync(frames[i], cancellation.Token); }
                catch (BftProtocolException)
                {
                    protocolError = true;
                    break;
                }
                if (sentGoAway && i == 0)
                {
                    Assert.Equal((uint)lastStreamId.GetInt32(), tunnel.Server.LastOpenedStreamId);
                    await tunnel.Server.SendGoAwayAsync(BftGoAwayCode.Shutdown, cancellation.Token);
                }
            }

            if (expected == "protocol-error")
                Assert.True(protocolError, sequence.GetProperty("name").GetString());
            else
            {
                Assert.False(protocolError, sequence.GetProperty("name").GetString());
                Assert.Equal(expected switch
                {
                    "process" => BftMuxFrameAction.Processed,
                    "ignore" => BftMuxFrameAction.Ignored,
                    "reset:3" => BftMuxFrameAction.ResetFlowControl,
                    "reset:7" => BftMuxFrameAction.ResetStreamClosed,
                    _ => throw new InvalidOperationException($"Unknown mux expectation '{expected}'.")
                }, action);
            }
        }
    }

    private static async Task<TunnelPair> ConnectAsync(int initialWindow, CancellationToken cancellationToken)
    {
        var exitPrivate = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var clientEphemeral = Enumerable.Range(0x61, 32).Select(value => (byte)value).ToArray();
        var serverEphemeral = Enumerable.Range(0x81, 32).Select(value => (byte)value).ToArray();
        var obfsKey = Enumerable.Range(0x20, 32).Select(value => (byte)value).ToArray();
        var (clientStream, serverStream) = TestDuplexStream.CreatePair();
        var serverTask = BftServerTunnel.TryAcceptAsync(serverStream, new BftServerTunnelOptions
        {
            Handshake = new BftServerHandshakeOptions
            {
                StaticPrivateKey = exitPrivate,
                ObfsKey = obfsKey,
                EphemeralPrivateKeyFactory = () => serverEphemeral.ToArray()
            },
            ProxyId = "lv-1",
            ServiceIds = [1, 4, 5],
            TlsSpkiSha256 = new byte[32],
            InitialWindow = initialWindow
        }, cancellationToken).AsTask();
        var clientTask = BftClientTunnel.ConnectAsync(clientStream, new BftClientTunnelOptions
        {
            Handshake = new BftClientHandshakeOptions
            {
                StaticPublicKey = BftCrypto.GetX25519PublicKey(exitPrivate),
                ObfsKey = obfsKey,
                EphemeralPrivateKeyFactory = () => clientEphemeral.ToArray()
            }
        }, cancellationToken).AsTask();
        await Task.WhenAll(serverTask, clientTask);
        var result = await serverTask;
        Assert.True(result.Accepted, result.HandshakeRejection?.ToString());
        return new TunnelPair(await clientTask, result.Tunnel!, clientStream, serverStream);
    }

    private sealed class TunnelPair(BftClientTunnel client, BftServerTunnel server, TestDuplexStream clientStream, TestDuplexStream serverStream) : IAsyncDisposable
    {
        public BftClientTunnel Client { get; } = client;
        public BftServerTunnel Server { get; } = server;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            await clientStream.DisposeAsync();
            await serverStream.DisposeAsync();
        }
    }
}
