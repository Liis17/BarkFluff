using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using BarkFluff.Bft;
using BarkFluff.BftProxy;
using Xunit;

namespace BarkFluff.BftProxy.Tests;

public sealed class BftProxyFixture : IAsyncLifetime
{
    public static readonly byte[] StaticPrivateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    public static readonly byte[] ObfsKey = Enumerable.Range(0x30, 32).Select(value => (byte)value).ToArray();
    public static readonly byte[] RevocableObfsKey = Enumerable.Range(0x70, 32).Select(value => (byte)value).ToArray();
    private string _directory = string.Empty;

    internal TestUpstream Upstream { get; private set; } = null!;
    internal BftProxyHost Proxy { get; private set; } = null!;
    internal ManualTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
    internal TestRandomSource Random { get; } = new();
    internal string KeyFile { get; private set; } = string.Empty;
    internal string ProxySecret { get; } = new('a', 64);
    internal byte[] RelayKey { get; } = Enumerable.Range(0x50, 32).Select(value => (byte)value).ToArray();

    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"bftproxy-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        KeyFile = Path.Combine(_directory, "clients.keys");
        await File.WriteAllTextAsync(KeyFile, $"integration-user {Encode(ObfsKey)}\nrevocable-user {Encode(RevocableObfsKey)}\n");
        Upstream = await TestUpstream.StartAsync();
        var upstreams = new Dictionary<byte, Uri>
        {
            [1] = Upstream.Address,
            [2] = Upstream.Address,
            [4] = Upstream.Address,
            [5] = Upstream.Address,
            [7] = Upstream.Address
        };
        var config = new ExitConfiguration(new ProxyEndpoint("127.0.0.1", 0), new ProxyEndpoint("127.0.0.1", 0), StaticPrivateKey,
            KeyFile, [new BftServerObfsKey("integration-user", ObfsKey), new BftServerObfsKey("revocable-user", RevocableObfsKey)], [], new HashSet<IPAddress> { IPAddress.Loopback }, RelayKey,
            "test-proxy", ProxySecret, upstreams);
        Proxy = await BftProxyHost.StartExitAsync(config, Time, Random, VerifyUpstreamCertificate);
    }

    internal async Task<BftTestClient> ConnectAsync(CancellationToken cancellationToken = default, byte[]? salt = null, byte[]? obfsKey = null)
    {
        var socket = new TcpClient();
        await socket.ConnectAsync(Proxy.ListenEndPoint, cancellationToken);
        return await ConnectAsync(socket, cancellationToken, salt, obfsKey);
    }

    internal async Task<BftTestClient> ConnectViaRelayAsync(BftRelayHost relay, CancellationToken cancellationToken = default, byte[]? salt = null, byte[]? obfsKey = null)
    {
        var socket = new TcpClient();
        await socket.ConnectAsync(relay.ListenEndPoint, cancellationToken);
        return await ConnectAsync(socket, cancellationToken, salt, obfsKey);
    }

    private async Task<BftTestClient> ConnectAsync(TcpClient socket, CancellationToken cancellationToken, byte[]? salt, byte[]? obfsKey)
    {
        var tunnel = await BftClientTunnel.ConnectAsync(socket.GetStream(), new BftClientTunnelOptions
        {
            Handshake = new BftClientHandshakeOptions
            {
                StaticPublicKey = BftCrypto.GetX25519PublicKey(StaticPrivateKey),
                ObfsKey = obfsKey ?? ObfsKey,
                Salt = salt
            }
        }, cancellationToken);
        return new BftTestClient(socket, tunnel);
    }

    public bool VerifyUpstreamCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        certificate is not null && certificate.GetRawCertData().AsSpan().SequenceEqual(Upstream.Certificate.RawData);

    public async Task DisposeAsync()
    {
        if (Proxy is not null) await Proxy.DisposeAsync();
        if (Upstream is not null) await Upstream.DisposeAsync();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class TestRandomSource : IBftRandomSource
{
    private readonly Channel<byte> _singleByteValues = Channel.CreateUnbounded<byte>();
    private int _sequence;

    public void Fill(Span<byte> destination)
    {
        if (destination.Length == 1)
        {
            destination[0] = 0;
            _singleByteValues.Writer.TryWrite(destination[0]);
            return;
        }
        for (var index = 0; index < destination.Length; index++)
            destination[index] = (byte)Interlocked.Increment(ref _sequence);
    }

    public Task<byte> WaitForSingleByteFillAsync(CancellationToken cancellationToken) =>
        _singleByteValues.Reader.ReadAsync(cancellationToken).AsTask();

    public void DrainSingleByteFills()
    {
        while (_singleByteValues.Reader.TryRead(out _)) { }
    }
}

internal sealed class BftTestClient(TcpClient socket, BftClientTunnel tunnel) : IAsyncDisposable
{
    public BftClientTunnel Tunnel { get; } = tunnel;

    public HttpClient CreateHttpClient(byte serviceId, Version version)
    {
        var handler = CreateHandler(serviceId);
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://bft.internal"),
            DefaultRequestVersion = version,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
    }

    public Grpc.Net.Client.GrpcChannel CreateGrpcChannel(byte serviceId) => Grpc.Net.Client.GrpcChannel.ForAddress(
        "https://bft.internal", new Grpc.Net.Client.GrpcChannelOptions { HttpHandler = CreateHandler(serviceId) });

    private SocketsHttpHandler CreateHandler(byte serviceId) => new()
    {
        UseProxy = false,
        EnableMultipleHttp2Connections = true,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (_, cancellationToken) => await Tunnel.OpenStreamAsync(serviceId, cancellationToken).ConfigureAwait(false),
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = VerifyPin
        }
    };

    private bool VerifyPin(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null) return false;
        using var cert = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
        var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
        try { return CryptographicOperations.FixedTimeEquals(SHA256.HashData(spki), Tunnel.Hello.TlsSpkiSha256); }
        finally { CryptographicOperations.ZeroMemory(spki); }
    }

    public async ValueTask DisposeAsync()
    {
        await Tunnel.DisposeAsync();
        socket.Dispose();
    }
}
