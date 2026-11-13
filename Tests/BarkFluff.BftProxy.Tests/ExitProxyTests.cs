using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using BarkFluff.Bft;
using Grpc.Core;
using Xunit;

namespace BarkFluff.BftProxy.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BftProxyTestCollection : ICollectionFixture<BftProxyFixture>
{
    public const string Name = "BFT proxy integration";
}

[Collection(BftProxyTestCollection.Name)]
public sealed class ExitProxyTests(BftProxyFixture fixture)
{
    [Fact]
    public async Task InternalTlsUsesBftInternalNameAndHelloSpkiPin()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await fixture.ConnectAsync(timeout.Token);
        var tunnelStream = await client.Tunnel.OpenStreamAsync(2, timeout.Token);
        using var tls = new SslStream(tunnelStream, leaveInnerStreamOpen: false, (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            using var cert = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
            var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
            try { return CryptographicOperations.FixedTimeEquals(SHA256.HashData(spki), client.Tunnel.Hello.TlsSpkiSha256); }
            finally { CryptographicOperations.ZeroMemory(spki); }
        });
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "bft.internal",
            ApplicationProtocols = [SslApplicationProtocol.Http11]
        }, timeout.Token);
        Assert.Equal(SslApplicationProtocol.Http11, tls.NegotiatedApplicationProtocol);
    }

    [Fact]
    public async Task Http11_UsesOnlyConfiguredServiceAndConnectionFeatureForTrustedHeaders()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await fixture.ConnectAsync(timeout.Token);
        Assert.Equal(new byte[] { 1, 2, 4, 5, 7 }, client.Tunnel.Hello.ServiceIds);

        using var http = client.CreateHttpClient(2, HttpVersion.Version11);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://attacker.example/headers")
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Host = "spoofed.example";
        request.Headers.TryAddWithoutValidation("x-auth-token", "client-token");
        request.Headers.TryAddWithoutValidation("X-Real-IP", "192.0.2.44");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "192.0.2.44");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "attacker.example");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        request.Headers.TryAddWithoutValidation("Forwarded", "for=192.0.2.44");
        request.Headers.TryAddWithoutValidation("CF-Connecting-IP", "192.0.2.44");
        request.Headers.TryAddWithoutValidation("True-Client-IP", "192.0.2.44");
        request.Headers.TryAddWithoutValidation("X-BFT-Spoof", "attacker-value");

        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var headers = json.RootElement;
        Assert.Equal("HTTP/1.1", headers.GetProperty("protocol").GetString());
        Assert.Equal(fixture.Upstream.Address.Authority, headers.GetProperty("host").GetString());
        Assert.Equal("127.0.0.1", headers.GetProperty("clientIp").GetString());
        Assert.Equal("test-proxy", headers.GetProperty("proxyId").GetString());
        Assert.Equal(fixture.ProxySecret, headers.GetProperty("proxySecret").GetString());
        Assert.Equal("client-token", headers.GetProperty("authToken").GetString());
        Assert.Equal(string.Empty, headers.GetProperty("realIp").GetString());
        Assert.Equal(string.Empty, headers.GetProperty("forwardedFor").GetString());
        Assert.Equal(string.Empty, headers.GetProperty("forwardedHost").GetString());
        Assert.Equal(string.Empty, headers.GetProperty("forwardedProto").GetString());
        Assert.Equal(string.Empty, headers.GetProperty("forwarded").GetString());
        Assert.Equal(string.Empty, headers.GetProperty("cloudflareIp").GetString());
        Assert.Equal(string.Empty, headers.GetProperty("spoofedBft").GetString());
    }

    [Fact]
    public async Task Grpc_UsesHttp2AndPreservesStreamingTrailersStatusDeadlineAndCancellation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var bft = await fixture.ConnectAsync(timeout.Token);
        using var channel = bft.CreateGrpcChannel(1);
        var grpc = new ProxyTest.ProxyTestClient(channel);

        var unary = grpc.UnaryAsync(new EchoRequest { Value = "hello" }, cancellationToken: timeout.Token);
        var unaryReply = await unary.ResponseAsync;
        Assert.Equal("hello|HTTP/2", unaryReply.Value);
        Assert.Equal("unary-done", unary.GetTrailers().GetValue("probe-trailer"));

        using (var serverCall = grpc.ServerStream(new EchoRequest { Value = "item" }, cancellationToken: timeout.Token))
        {
            var values = new List<string>();
            while (await serverCall.ResponseStream.MoveNext(timeout.Token)) values.Add(serverCall.ResponseStream.Current.Value);
            Assert.Equal(["item-0", "item-1", "item-2"], values);
            Assert.Equal("stream-done", serverCall.GetTrailers().GetValue("probe-trailer"));
        }

        using (var duplex = grpc.Duplex(cancellationToken: timeout.Token))
        {
            await duplex.RequestStream.WriteAsync(new EchoRequest { Value = "one" });
            await duplex.RequestStream.WriteAsync(new EchoRequest { Value = "two" });
            await duplex.RequestStream.CompleteAsync();
            var values = new List<string>();
            while (await duplex.ResponseStream.MoveNext(timeout.Token)) values.Add(duplex.ResponseStream.Current.Value);
            Assert.Equal(["echo:one", "echo:two"], values);
            Assert.Equal("duplex-done", duplex.GetTrailers().GetValue("probe-trailer"));
        }

        var failure = grpc.UnaryAsync(new EchoRequest { Value = "failure" }, cancellationToken: timeout.Token);
        var status = await Assert.ThrowsAsync<RpcException>(() => failure.ResponseAsync);
        Assert.Equal(StatusCode.ResourceExhausted, status.StatusCode);
        Assert.Equal("upstream rejected", status.Status.Detail);
        Assert.Equal("failure", status.Trailers.GetValue("probe-trailer"));

        var deadline = grpc.UnaryAsync(new EchoRequest { Value = "wait" }, deadline: DateTime.UtcNow.AddSeconds(1));
        var deadlineError = await Assert.ThrowsAsync<RpcException>(() => deadline.ResponseAsync);
        Assert.Equal(StatusCode.DeadlineExceeded, deadlineError.StatusCode);
        Assert.Equal("wait", await fixture.Upstream.WaitForCancellationAsync(timeout.Token));

        using var callCancellation = new CancellationTokenSource();
        using var cancelled = grpc.ServerStream(new EchoRequest { Value = "cancel" }, cancellationToken: callCancellation.Token);
        Assert.True(await cancelled.ResponseStream.MoveNext(timeout.Token));
        callCancellation.Cancel();
        await fixture.Upstream.WaitForCancellationAsync(timeout.Token);
    }

    [Fact]
    public async Task Http11StreamingTransfers32MiBAndOtherServiceContinuesDuringUpload()
    {
        const int totalBytes = 32 * 1024 * 1024;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var bft = await fixture.ConnectAsync(timeout.Token);
        using var uploadClient = bft.CreateHttpClient(5, HttpVersion.Version11);
        using var realtimeClient = bft.CreateHttpClient(7, HttpVersion.Version11);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var upload = new StreamContent(new GeneratedUploadStream(totalBytes, gate.Task));
        upload.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var uploadTask = uploadClient.PostAsync("/upload", upload, timeout.Token);
        await fixture.Upstream.FirstUploadByte.WaitAsync(timeout.Token);
        Assert.False(uploadTask.IsCompleted);

        using var realtime = await realtimeClient.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        realtime.EnsureSuccessStatusCode();
        var events = await realtime.Content.ReadAsStringAsync(timeout.Token);
        Assert.Contains("data: 0", events);
        Assert.Contains("data: 4", events);

        gate.TrySetResult();
        using var uploadResponse = await uploadTask;
        uploadResponse.EnsureSuccessStatusCode();
        using var uploadJson = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(totalBytes, uploadJson.RootElement.GetProperty("length").GetInt64());
        Assert.Equal(HashRepeated(0xA5, totalBytes), uploadJson.RootElement.GetProperty("hash").GetString());

        using var downloadResponse = await realtimeClient.GetAsync("/download", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        downloadResponse.EnsureSuccessStatusCode();
        var download = await downloadResponse.Content.ReadAsStreamAsync(timeout.Token);
        await fixture.Upstream.FirstDownloadChunk.WaitAsync(timeout.Token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        var total = 0;
        var firstRead = await download.ReadAsync(buffer, timeout.Token);
        Assert.True(firstRead > 0);
        hash.AppendData(buffer.AsSpan(0, firstRead));
        total += firstRead;
        Assert.False(fixture.Upstream.DownloadCompleted.IsCompleted);
        fixture.Upstream.ReleaseDownload();
        while (true)
        {
            var count = await download.ReadAsync(buffer, timeout.Token);
            if (count == 0) break;
            hash.AppendData(buffer.AsSpan(0, count));
            total += count;
        }
        Assert.Equal(totalBytes, total);
        Assert.Equal(HashRepeated(0x5A, totalBytes), Convert.ToHexString(hash.GetHashAndReset()));
    }

    [Fact]
    public async Task UpstreamAbortResetsOnlyItsStreamAndCliManagesClientLinks()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var bft = await fixture.ConnectAsync(timeout.Token);
        using var http = bft.CreateHttpClient(4, HttpVersion.Version11);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync("/abort", timeout.Token));
        Assert.False(bft.Tunnel.Completion.IsCompleted);

        using var channel = bft.CreateGrpcChannel(1);
        var reply = await new ProxyTest.ProxyTestClient(channel).UnaryAsync(new EchoRequest { Value = "after-abort" }, cancellationToken: timeout.Token).ResponseAsync;
        Assert.Equal("after-abort|HTTP/2", reply.Value);

        var previousOut = Console.Out;
        var priorFile = Environment.GetEnvironmentVariable("BFT_CLIENT_KEYS_FILE");
        var priorPrivate = Environment.GetEnvironmentVariable("BFT_STATIC_PRIVATE_KEY");
        var cliFile = Path.Combine(Path.GetDirectoryName(fixture.KeyFile)!, "cli.keys");
        try
        {
            Environment.SetEnvironmentVariable("BFT_CLIENT_KEYS_FILE", cliFile);
            Environment.SetEnvironmentVariable("BFT_STATIC_PRIVATE_KEY", Encode(BftProxyFixture.StaticPrivateKey));
            using var captured = new StringWriter();
            Console.SetOut(captured);
            Assert.Equal(0, await BftProxyCli.RunAsync(["genkey"]));
            var generated = captured.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            var privateKey = Decode(generated[0]["BFT_STATIC_PRIVATE_KEY=".Length..]);
            Assert.Equal(Encode(BftCrypto.GetX25519PublicKey(privateKey)), generated[1]["public=".Length..]);
            Environment.SetEnvironmentVariable("BFT_STATIC_PRIVATE_KEY", Encode(privateKey));

            captured.GetStringBuilder().Clear();
            var cliListenPort = ReservePort();
            Assert.Equal(0, await BftProxyCli.RunAsync(["add-client", "--label", "cli-user", "--name", "My proxy", "--host", "127.0.0.1", "--port", cliListenPort.ToString(), "--token", "my token"]));
            var link = captured.ToString().Trim();
            var parsed = BftConnectionString.Parse(link);
            Assert.Equal("My proxy", parsed.Name);
            Assert.Equal("my token", parsed.Token);
            Assert.Contains("cli-user", await File.ReadAllTextAsync(cliFile));

            var clientKeysBytes = await File.ReadAllBytesAsync(cliFile);
            IReadOnlyList<BftServerObfsKey> clientKeys;
            try { clientKeys = ClientKeyFile.Parse(clientKeysBytes); }
            finally { CryptographicOperations.ZeroMemory(clientKeysBytes); }
            await using var cliProxy = await BftProxyHost.StartExitAsync(new ExitConfiguration(
                new ProxyEndpoint("127.0.0.1", cliListenPort), null, privateKey, cliFile, clientKeys, [parsed.Token!],
                new HashSet<IPAddress>(), null, "cli-proxy", fixture.ProxySecret, new Dictionary<byte, Uri>()),
                fixture.Time, fixture.Random);
            using var cliSocket = new TcpClient();
            await cliSocket.ConnectAsync(parsed.Host, parsed.Port, timeout.Token);
            await using var cliTunnel = await BftClientTunnel.ConnectAsync(cliSocket.GetStream(), new BftClientTunnelOptions
            {
                Handshake = new BftClientHandshakeOptions { StaticPublicKey = parsed.StaticPublicKey, ObfsKey = parsed.ObfsKey },
                Token = parsed.Token
            }, timeout.Token);
            Assert.Equal("cli-proxy", cliTunnel.Hello.ProxyId);
            var revoked = cliTunnel.WaitForGoAwayAsync(timeout.Token);

            captured.GetStringBuilder().Clear();
            Assert.Equal(0, await BftProxyCli.RunAsync(["remove-client", "--label", "cli-user"]));
            Assert.DoesNotContain("cli-user", await File.ReadAllTextAsync(cliFile));
            fixture.Time.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(0u, await revoked);
        }
        finally
        {
            Console.SetOut(previousOut);
            Environment.SetEnvironmentVariable("BFT_CLIENT_KEYS_FILE", priorFile);
            Environment.SetEnvironmentVariable("BFT_STATIC_PRIVATE_KEY", priorPrivate);
        }
    }

    private static string HashRepeated(byte value, int totalBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var block = new byte[64 * 1024];
        Array.Fill(block, value);
        for (var remaining = totalBytes; remaining > 0; remaining -= block.Length)
            hash.AppendData(block, 0, Math.Min(block.Length, remaining));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");

    private static int ReservePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class GeneratedUploadStream : Stream
    {
        private readonly int _length;
        private readonly Task _gate;
        private readonly byte[] _chunk = FilledChunk();
        private int _remaining;
        private bool _first = true;

        public GeneratedUploadStream(int length, Task gate)
        {
            _length = length;
            _remaining = length;
            _gate = gate;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_first) await _gate.WaitAsync(cancellationToken);
            if (_remaining == 0) return 0;
            var count = Math.Min(Math.Min(buffer.Length, _chunk.Length), _remaining);
            _chunk.AsMemory(0, count).CopyTo(buffer);
            _remaining -= count;
            _first = false;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private static byte[] FilledChunk() { var bytes = new byte[64 * 1024]; Array.Fill(bytes, (byte)0xA5); return bytes; }
    }
}
