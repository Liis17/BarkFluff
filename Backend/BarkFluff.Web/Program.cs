using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

using Serilog;

using System.Net;
using System.Text;
using System.Text.Json;

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

// CORS — разрешаем обращения из настроенных origin'ов; также публикуем gRPC-Web-трейлеры.
// В проде список обязателен; localhost-fallback оставляем только для разработки.
var allowedOrigins = builder.Configuration.GetSection("Web:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();
if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
    allowedOrigins = new[] { "http://localhost:7016" };

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "OPTIONS")
    .WithHeaders(
        "content-type",
        "x-grpc-web",
        "x-user-agent",
        "x-auth-token",
        "x-device-id",
        "x-device-name",
        "x-ip",
        "x-os",
        "x-app-name",
        "x-app-version")
    .AllowCredentials()
    .WithExposedHeaders(
        "grpc-status",
        "grpc-message",
        "grpc-status-details-bin",
        "x-error-code")));

// Доверенные reverse-proxy (nginx, Docker internal networks) — источник X-Forwarded-For.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    // Docker bridge / internal networks
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// YARP reverse-proxy — по одному кластеру на каждый backend-сервис
// gRPC-Web middleware (ниже) превращает входящий HTTP/1.1 gRPC-Web запрос
// в обычный gRPC HTTP/2, после чего YARP форвардит его в соответствующий сервис.
builder.Services.AddReverseProxy()
    .LoadFromMemory(BuildRoutes(), BuildClusters(builder.Configuration));

builder.Services.AddHealthChecks();

var app = builder.Build();

// Kestrel по умолчанию ограничивает тело запроса примерно 28.6 МБ.
// Поднимаем лимит только для проксируемой HTTP-загрузки, до первого чтения тела.
app.Use((ctx, next) =>
{
    if (HttpMethods.IsPost(ctx.Request.Method) &&
        ctx.Request.Path.StartsWithSegments("/api/files/upload"))
    {
        var maxRequestBodySize = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySize is { IsReadOnly: false })
            maxRequestBodySize.MaxRequestBodySize = 512L * 1024 * 1024;
    }

    return next();
});

// Применяем X-Forwarded-For/-Proto только от доверенных прокси (см. KnownNetworks выше).
// После этого ctx.Connection.RemoteIpAddress = реальный IP клиента; подделать нельзя.
app.UseForwardedHeaders();

// Логирование и метрики HTTP-запросов
app.Use(async (ctx, next) =>
{
    var metrics = ctx.RequestServices.GetRequiredService<MetricsCollector>();
    metrics.Increment("http_requests_total");

    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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

// Стандартные security-заголовки для всех ответов.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        if (!h.ContainsKey("X-Content-Type-Options")) h["X-Content-Type-Options"] = "nosniff";
        if (!h.ContainsKey("Referrer-Policy")) h["Referrer-Policy"] = "same-origin";
        if (!h.ContainsKey("X-Frame-Options")) h["X-Frame-Options"] = "DENY";
        return Task.CompletedTask;
    });
    await next();
});

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
// Защита от гигантских grpc-web-text тел: 4 МБ с запасом на base64-оверхед.
const long MAX_GRPC_WEB_REQUEST_BYTES = 4L * 1024 * 1024;

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

    if (ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value > MAX_GRPC_WEB_REQUEST_BYTES)
    {
        ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    // --- ЗАПРОС: декодируем base64 тело ---
    if (isGrpcWebText)
    {
        using var reqMs = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(reqMs);
        if (reqMs.Length > MAX_GRPC_WEB_REQUEST_BYTES)
        {
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
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
        // Запрещаем nginx буферизировать ответ — критично для server-streaming
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        return Task.CompletedTask;
    });

    // Подменяем Response.Body на потоковый враппер, который каждый Write
    // сразу кодирует в base64 (для grpc-web-text) и пишет в оригинальный поток.
    // Это критично для server-streaming: данные приходят по частям.
    var grpcWebStream = new GrpcWebResponseStream(originalResponseBody, isGrpcWebText);
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

app.MapHealthChecks("/health");

// Firebase web configuration and the VAPID key are public client identifiers.
// Service-account credentials deliberately remain exclusive to CloudMessaging.
app.MapGet("/pwa-config.js", (HttpContext ctx, IConfiguration configuration) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    var firebase = configuration.GetSection("Web:Push:Firebase");
    var vapidKey = configuration["Web:Push:VapidKey"];
    var apiKey = firebase["ApiKey"];
    var authDomain = firebase["AuthDomain"];
    var projectId = firebase["ProjectId"];
    var messagingSenderId = firebase["MessagingSenderId"];
    var appId = firebase["AppId"];

    if (new[] { vapidKey, apiKey, authDomain, projectId, messagingSenderId, appId }
        .Any(string.IsNullOrWhiteSpace))
    {
        return Results.Text("self.BF_PWA_CONFIG = null;", "application/javascript", Encoding.UTF8);
    }

    var script = "self.BF_PWA_CONFIG = " + JsonSerializer.Serialize(new
    {
        firebase = new
        {
            apiKey,
            authDomain,
            projectId,
            storageBucket = firebase["StorageBucket"] ?? string.Empty,
            messagingSenderId,
            appId
        },
        vapidKey
    }) + ";";
    return Results.Text(script, "application/javascript", Encoding.UTF8);
});

app.MapGet("/messenger", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "messenger.html"), "text/html"));

app.MapReverseProxy();

// Fallback: SPA отдаётся для любого URL, КРОМЕ /api/* и /health —
// чтобы неверный HTTP-метод на upload/health не маскировался index.html.
app.MapFallback(async (HttpContext ctx, IWebHostEnvironment env) =>
{
    var path = ctx.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html").ExecuteAsync(ctx);
});

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
        ("fast-auth", "fast-auth", "/barkfluff.fast.auth.FastAuthApi/{**catchall}"),
        ("calls",     "calls",     "/barkfluff.calls.CallsApi/{**catchall}"),
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
        ("fast-auth", "FastAuthService:Host",  "http://fast-auth:7008"),
        ("calls",     "CallsService:Host",     "http://calls:7025"),
    };

    // Сервисы с server-streaming RPC: YARP не должен убивать долгоживущие соединения.
    // calls — долгий SubscribeCallEvents (доставка входящих звонков, device-scope).
    var streamingServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "updates", "onliner", "fast-auth", "calls" };

    var clusters = new List<ClusterConfig>();
    foreach (var (clusterId, configKey, defaultHost) in grpcClusters)
    {
        var host = config[configKey] ?? defaultHost;
        var isStreaming = streamingServices.Contains(clusterId);
        clusters.Add(new ClusterConfig
        {
            ClusterId = clusterId,
            HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
            {
                Version = new Version(2, 0),
                VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
                // Для server-streaming: увеличиваем таймаут активности до 24 часов,
                // иначе YARP убивает соединение через ~100с (дефолт).
                ActivityTimeout = isStreaming
                    ? TimeSpan.FromHours(24)
                    : TimeSpan.FromSeconds(100)
            },
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [$"{clusterId}-1"] = new DestinationConfig { Address = host }
            }
        });
    }

    // Отдельный cluster для файлового upload (обычный REST POST).
    // HTTP/1.1-порт Files-сервиса (порт для REST-контроллера загрузки файлов).
    // FilesService:Host — gRPC-порт (HTTP/2 only), не подходит для multipart upload.
    // FilesService:HttpHost — явно заданный HTTP/1-порт (7006 по умолчанию).
    var filesHttpHost = config["FilesService:HttpHost"] ?? "http://files:7006";
    clusters.Add(new ClusterConfig
    {
        ClusterId = "files-http",
        HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
        {
            Version = new Version(1, 1),
            VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower
        },
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["files-http-1"] = new DestinationConfig { Address = filesHttpHost }
        }
    });

    return clusters;
}

/// <summary>
/// Потоковый враппер для конвертации gRPC → gRPC-Web(-Text).
///
/// Для grpc-web-text каждый Write кодируется как независимый base64-чанк
/// с padding'ом и сразу флашится. По спеке gRPC-Web тело может состоять из
/// конкатенации таких чанков ('=' в середине потока — законный разделитель),
/// декодер grpc-web JS обрабатывает каждую 4-символьную группу независимо.
/// Это критично для server-streaming: буферизация остатка до следующего Write
/// задерживала бы хвост фрейма до следующего события.
///
/// Для grpc-web (бинарный) данные проходят насквозь.
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

        if (!_base64)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            await _inner.FlushAsync(cancellationToken);
            return;
        }

        // Независимый base64-чанк с padding'ом — фрейм уходит клиенту целиком сразу
        var b64 = Convert.ToBase64String(buffer.Span);
        var bytes = Encoding.ASCII.GetBytes(b64);
        await _inner.WriteAsync(bytes, cancellationToken);
        await _inner.FlushAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing) { /* не закрываем _inner */ }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
