using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using BarkFluff.BftProxy.Tests;
using Grpc.Core;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.BftProxy.Tests;

internal sealed class TestUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Channel<string> _cancellations;
    private readonly TaskCompletionSource _firstUploadByte = NewSignal();
    private readonly TaskCompletionSource _firstDownloadChunk = NewSignal();
    private readonly TaskCompletionSource _downloadGate = NewSignal();
    private readonly TaskCompletionSource _downloadCompleted = NewSignal();
    private readonly TaskCompletionSource _eventsStarted = NewSignal();

    private TestUpstream(WebApplication app, X509Certificate2 certificate, Uri address, Channel<string> cancellations)
    {
        _app = app;
        Certificate = certificate;
        Address = address;
        _cancellations = cancellations;
    }

    public X509Certificate2 Certificate { get; }
    public Uri Address { get; }
    public Task FirstUploadByte => _firstUploadByte.Task;
    public Task FirstDownloadChunk => _firstDownloadChunk.Task;
    public Task DownloadCompleted => _downloadCompleted.Task;
    public Task EventsStarted => _eventsStarted.Task;
    public void ReleaseDownload() => _downloadGate.TrySetResult();
    public Task<string> WaitForCancellationAsync(CancellationToken cancellationToken) => _cancellations.Reader.ReadAsync(cancellationToken).AsTask();

    public static async Task<TestUpstream> StartAsync()
    {
        var certificate = CreateCertificate();
        var port = ReservePort();
        var cancellations = Channel.CreateUnbounded<string>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = null;
            options.Listen(IPAddress.Loopback, port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.UseHttps(certificate);
            });
        });
        builder.Services.AddGrpc();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(cancellations);
        var app = builder.Build();
        var instance = new TestUpstream(app, certificate, new Uri($"https://127.0.0.1:{port}"), cancellations);

        app.MapGrpcService<ProxyTestGrpcService>();
        app.MapGet("/headers", (HttpContext context) => Results.Json(new
        {
            host = context.Request.Host.Value,
            protocol = context.Request.Protocol,
            clientIp = context.Request.Headers["X-BFT-Client-IP"].ToString(),
            proxyId = context.Request.Headers["X-BFT-Proxy-Id"].ToString(),
            proxySecret = context.Request.Headers["X-BFT-Proxy-Secret"].ToString(),
            authToken = context.Request.Headers["x-auth-token"].ToString(),
            realIp = context.Request.Headers["X-Real-IP"].ToString(),
            forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString(),
            forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString(),
            forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString(),
            forwarded = context.Request.Headers["Forwarded"].ToString(),
            cloudflareIp = context.Request.Headers["CF-Connecting-IP"].ToString(),
            spoofedBft = context.Request.Headers["X-BFT-Spoof"].ToString()
        }));
        app.MapPost("/upload", async context =>
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long length = 0;
            try
            {
                while (true)
                {
                    var count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
                    if (count == 0) break;
                    instance._firstUploadByte.TrySetResult();
                    hash.AppendData(buffer.AsSpan(0, count));
                    length += count;
                }
                await context.Response.WriteAsJsonAsync(new { length, hash = Convert.ToHexString(hash.GetHashAndReset()) }, context.RequestAborted);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        });
        app.MapGet("/download", async context =>
        {
            context.Response.ContentType = "application/octet-stream";
            var buffer = new byte[64 * 1024];
            Array.Fill(buffer, (byte)0x5A);
            const int chunks = 512;
            for (var i = 0; i < chunks; i++)
            {
                await context.Response.Body.WriteAsync(buffer, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                if (i == 0)
                {
                    instance._firstDownloadChunk.TrySetResult();
                    await instance._downloadGate.Task.WaitAsync(context.RequestAborted);
                }
            }
            instance._downloadCompleted.TrySetResult();
        });
        app.MapGet("/events", async context =>
        {
            context.Response.ContentType = "text/event-stream";
            for (var i = 0; i < 5; i++)
            {
                await context.Response.WriteAsync($"data: {i}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                if (i == 0) instance._eventsStarted.TrySetResult();
            }
        });
        app.MapGet("/abort", context =>
        {
            context.Abort();
            return Task.CompletedTask;
        });

        await app.StartAsync();
        return instance;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Certificate.Dispose();
    }

    private static int ReservePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
        if (!OperatingSystem.IsWindows()) return generated;
        var pfx = generated.Export(X509ContentType.Pfx);
        generated.Dispose();
        try { return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ProxyTestGrpcService(IHttpContextAccessor accessor, Channel<string> cancellations) : ProxyTest.ProxyTestBase
    {
        public override async Task<EchoReply> Unary(EchoRequest request, ServerCallContext context)
        {
            if (request.Value == "failure")
                throw new RpcException(new Status(StatusCode.ResourceExhausted, "upstream rejected"), new Metadata { { "probe-trailer", "failure" } });
            if (request.Value == "wait")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
                catch (OperationCanceledException)
                {
                    cancellations.Writer.TryWrite("wait");
                    throw;
                }
            }
            context.ResponseTrailers.Add("probe-trailer", "unary-done");
            return new EchoReply { Value = $"{request.Value}|{accessor.HttpContext!.Request.Protocol}" };
        }

        public override async Task ServerStream(EchoRequest request, IServerStreamWriter<EchoReply> responseStream, ServerCallContext context)
        {
            if (request.Value == "cancel")
            {
                await responseStream.WriteAsync(new EchoReply { Value = "first" });
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
                catch (OperationCanceledException)
                {
                    cancellations.Writer.TryWrite("cancelled");
                    throw;
                }
            }
            for (var index = 0; index < 3; index++)
                await responseStream.WriteAsync(new EchoReply { Value = $"{request.Value}-{index}" });
            context.ResponseTrailers.Add("probe-trailer", "stream-done");
        }

        public override async Task Duplex(IAsyncStreamReader<EchoRequest> requestStream, IServerStreamWriter<EchoReply> responseStream, ServerCallContext context)
        {
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                var request = requestStream.Current;
                await responseStream.WriteAsync(new EchoReply { Value = $"echo:{request.Value}" });
            }
            context.ResponseTrailers.Add("probe-trailer", "duplex-done");
        }
    }
}
