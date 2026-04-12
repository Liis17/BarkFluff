using System.Text;

using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

using Serilog;

using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.LoadConfiguration(ServiceId.Web);
builder.AddBarkFluffSerilog("BarkFluff.Web");

var port = int.TryParse(builder.Configuration["RunSettings:Port"], out var p) ? p : 7016;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddBarkFluffMetrics("BarkFluff.Web");

// CORS — разрешаем обращения из настроенных origin'ов; также публикуем gRPC-Web-трейлеры
var allowedOrigins = builder.Configuration.GetSection("Web:AllowedOrigins").Get<string[]>()
                     ?? new[] { "http://localhost:7016" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials()
    .WithExposedHeaders(
        "grpc-status",
        "grpc-message",
        "grpc-status-details-bin",
        "x-error-code")));

// YARP reverse-proxy — по одному кластеру на каждый backend-сервис
// gRPC-Web middleware (ниже) превращает входящий HTTP/1.1 gRPC-Web запрос
// в обычный gRPC HTTP/2, после чего YARP форвардит его в соответствующий сервис.
builder.Services.AddReverseProxy()
    .LoadFromMemory(BuildRoutes(), BuildClusters(builder.Configuration));

var app = builder.Build();

// Логирование и метрики HTTP-запросов
app.Use(async (ctx, next) =>
{
    var metrics = ctx.RequestServices.GetRequiredService<MetricsCollector>();
    metrics.Increment("http_requests_total");

    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    var ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
             ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
             ?? ctx.Connection.RemoteIpAddress?.ToString()
             ?? "unknown";

    logger.LogDebug("HTTP {Method} {Path} от {IP}", ctx.Request.Method, ctx.Request.Path, ip);

    try
    {
        await next();
    }
    catch (Exception ex)
    {
        metrics.Increment("http_requests_errors");
        logger.LogError(ex, "Ошибка при обработке {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        throw;
    }
});

app.UseCors();

// gRPC-Web ↔ gRPC конвертация для YARP.
//
// Браузер отправляет application/grpc-web-text (base64, HTTP/1.1).
// YARP форвардит как application/grpc (бинарный, HTTP/2) в бэкенд-сервисы.
//
// Grpc.AspNetCore.Web не работает с YARP: middleware не декодирует тело запроса,
// поэтому реализуем конвертацию вручную.
//
// Запрос: base64-декодируем тело, меняем Content-Type на application/grpc.
// Ответ: каждый data-frame сразу кодируется и отправляется клиенту (поддержка
//         server-streaming). Trailer-frame добавляется при завершении потока.
app.Use(async (ctx, next) =>
{
    var ct = ctx.Request.ContentType ?? "";
    var isGrpcWebText = ct.StartsWith("application/grpc-web-text", StringComparison.OrdinalIgnoreCase);
    var isGrpcWeb = isGrpcWebText || ct.StartsWith("application/grpc-web", StringComparison.OrdinalIgnoreCase);

    if (!isGrpcWeb)
    {
        await next();
        return;
    }

    // --- ЗАПРОС: декодируем base64 тело ---
    if (isGrpcWebText)
    {
        using var reqMs = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(reqMs);
        var b64 = Encoding.ASCII.GetString(reqMs.ToArray()).TrimEnd();
        if (b64.Length % 4 != 0)
            b64 += new string('=', 4 - b64.Length % 4);
        var decoded = Convert.FromBase64String(b64);
        ctx.Request.Body = new MemoryStream(decoded);
        ctx.Request.ContentLength = decoded.Length;
    }
    ctx.Request.ContentType = "application/grpc";

    // --- ОТВЕТ: потоковая конвертация gRPC → gRPC-Web ---
    var originalResponseBody = ctx.Response.Body;

    // Перехватываем grpc-трейлеры, которые YARP продвигает в заголовки ответа
    var promotedTrailers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    ctx.Response.OnStarting(() =>
    {
        foreach (var key in new[] { "grpc-status", "grpc-message", "grpc-status-details-bin", "x-error-code" })
        {
            if (ctx.Response.Headers.TryGetValue(key, out var val))
            {
                promotedTrailers[key] = val.ToString();
                ctx.Response.Headers.Remove(key);
            }
        }
        ctx.Response.Headers["Content-Type"] = isGrpcWebText
            ? "application/grpc-web-text+proto"
            : "application/grpc-web+proto";
        ctx.Response.Headers.Remove("Content-Length");
        return Task.CompletedTask;
    });

    // Подменяем Response.Body на потоковый враппер, который каждый Write
    // сразу кодирует в base64 (для grpc-web-text) и пишет в оригинальный поток.
    // Это критично для server-streaming: данные приходят по частям.
    await using var grpcWebStream = new GrpcWebResponseStream(originalResponseBody, isGrpcWebText);
    ctx.Response.Body = grpcWebStream;

    // Отключаем буферизацию ответа для потокового режима
    var bufferingFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
    bufferingFeature?.DisableBuffering();

    await next();

    ctx.Response.Body = originalResponseBody;

    // Собираем трейлеры и отправляем trailer-frame
    var trailerHeaders = new Dictionary<string, string>(promotedTrailers, StringComparer.OrdinalIgnoreCase);
    var trailerFeature = ctx.Features.Get<IHttpResponseTrailersFeature>();
    if (trailerFeature?.Trailers != null)
    {
        foreach (var kvp in trailerFeature.Trailers)
            trailerHeaders[kvp.Key.ToString()] = kvp.Value.ToString();
    }

    var trailerSb = new StringBuilder();
    foreach (var (k, v) in trailerHeaders)
        trailerSb.Append(k).Append(": ").Append(v).Append("\r\n");
    var trailerData = Encoding.UTF8.GetBytes(trailerSb.ToString());
    var trailerFrame = new byte[5 + trailerData.Length];
    trailerFrame[0] = 0x80;
    var lenBuf = BitConverter.GetBytes((uint)trailerData.Length);
    if (BitConverter.IsLittleEndian) Array.Reverse(lenBuf);
    lenBuf.CopyTo(trailerFrame, 1);
    trailerData.CopyTo(trailerFrame, 5);

    if (isGrpcWebText)
    {
        var b64Trailer = Encoding.ASCII.GetBytes(Convert.ToBase64String(trailerFrame));
        await originalResponseBody.WriteAsync(b64Trailer);
    }
    else
    {
        await originalResponseBody.WriteAsync(trailerFrame);
    }

    await originalResponseBody.FlushAsync();
});

app.UseStaticFiles();

app.MapGet("/messenger", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "messenger.html"), "text/html"));

app.MapReverseProxy();

app.MapFallbackToFile("index.html");

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();

// --- YARP routes / clusters ---

static IReadOnlyList<RouteConfig> BuildRoutes()
{
    // Полные имена gRPC-сервисов из *_api.proto (package + service).
    (string route, string cluster, string path)[] grpcRoutes =
    {
        ("identity",  "identity",  "/barkfluff.identity.IdentityApi/{**catchall}"),
        ("users",     "users",     "/barkfluff.users.UsersApi/{**catchall}"),
        ("messages",  "messages",  "/barkfluff.messages.MessagesApi/{**catchall}"),
        ("files",     "files",     "/barkfluff.files.FilesApi/{**catchall}"),
        ("updates",   "updates",   "/barkfluff.updates.UpdatesApi/{**catchall}"),
        ("onliner",   "onliner",   "/barkfluff.onliner.OnlinerApi/{**catchall}"),
    };

    var routes = new List<RouteConfig>();
    // Content-Length из оригинального grpcwebtext запроса (base64-размер) не соответствует
    // размеру декодированного бинарного тела — убираем, чтобы не было HTTP/2 protocol error на бэкенде.
    var grpcTransforms = new[]
    {
        new Dictionary<string, string> { { "RequestHeaderRemove", "Content-Length" } }
    };

    foreach (var (name, cluster, path) in grpcRoutes)
    {
        routes.Add(new RouteConfig
        {
            RouteId = $"grpc-{name}",
            ClusterId = cluster,
            Match = new RouteMatch { Path = path },
            Transforms = grpcTransforms
        });
    }

    // HTTP upload — client-streaming недоступен в gRPC-Web, поэтому используем прямой HTTP POST.
    routes.Add(new RouteConfig
    {
        RouteId = "files-http-upload",
        ClusterId = "files-http",
        Match = new RouteMatch
        {
            Path = "/api/files/upload/{uploadId}",
            Methods = new[] { "POST" }
        },
        Transforms = new[]
        {
            new Dictionary<string, string> { { "PathPattern", "/upload/{uploadId}" } }
        }
    });

    return routes;
}

static IReadOnlyList<ClusterConfig> BuildClusters(IConfiguration config)
{
    // HTTP/2 (h2c) cluster для gRPC-вызовов — сервисы слушают gRPC на основном порту.
    (string clusterId, string configKey, string defaultHost)[] grpcClusters =
    {
        ("identity",  "IdentityService:Host",  "http://identity:7000"),
        ("users",     "UsersService:Host",     "http://users:7001"),
        ("messages",  "MessagesService:Host",  "http://messages:7007"),
        ("files",     "FilesService:Host",     "http://files:7005"),
        ("updates",   "UpdatesService:Host",   "http://updates:7015"),
        ("onliner",   "OnlinerService:Host",   "http://onliner:7009"),
    };

    var clusters = new List<ClusterConfig>();
    foreach (var (clusterId, configKey, defaultHost) in grpcClusters)
    {
        var host = config[configKey] ?? defaultHost;
        clusters.Add(new ClusterConfig
        {
            ClusterId = clusterId,
            HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
            {
                Version = new Version(2, 0),
                VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact
            },
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [$"{clusterId}-1"] = new DestinationConfig { Address = host }
            }
        });
    }

    // Отдельный HTTP/1.1 cluster для файлового upload (обычный REST POST).
    var filesHttpHost = config["FilesService:HttpHost"]
                        ?? config["FilesService:Host"]
                        ?? "http://files:7005";
    clusters.Add(new ClusterConfig
    {
        ClusterId = "files-http",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["files-http-1"] = new DestinationConfig { Address = filesHttpHost }
        }
    });

    return clusters;
}

/// <summary>
/// Потоковый враппер для конвертации gRPC → gRPC-Web(-Text).
/// Каждый Write немедленно кодируется (base64 для grpc-web-text) и записывается
/// в нижележащий поток с flush — это обеспечивает работу server-streaming.
/// </summary>
sealed class GrpcWebResponseStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _base64;

    public GrpcWebResponseStream(Stream inner, bool base64)
    {
        _inner = inner;
        _base64 = base64;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return;

        if (_base64)
        {
            var b64 = Convert.ToBase64String(buffer.Span);
            var bytes = Encoding.ASCII.GetBytes(b64);
            await _inner.WriteAsync(bytes, cancellationToken);
        }
        else
        {
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        await _inner.FlushAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing) { /* не закрываем _inner */ }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
