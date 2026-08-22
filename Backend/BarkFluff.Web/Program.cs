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

// Web:Mode = Node (по умолчанию) — хост ноды: все кластеры сервисов, статика, upload.
//            Shell — глобальный web.barkfluff.com: только статика + прокси Navigator.
//            Proxy — зеркало одной ноды на доступном из РФ хосте: статика + pass-through
//            всего трафика (gRPC-Web, upload, ping) на публичный Web-шлюз ноды за
//            Cloudflare + media-relay файловых хостов (Web:Proxy:*).
// Читаем ДО LoadConfiguration: env-переменные CreateBuilder подхватывает сам.
var webMode = builder.Configuration["Web:Mode"];
var isShellMode = string.Equals(webMode, "Shell", StringComparison.OrdinalIgnoreCase);
var isProxyMode = string.Equals(webMode, "Proxy", StringComparison.OrdinalIgnoreCase);

// Шелл и прокси живут вне ноды, рядом с ними нет Configuration-сервиса, а
// LoadConfiguration падает без ретраев при недоступном сервисе — поэтому в этих
// режимах его пропускаем и берём конфигурацию только из env/appsettings (тот же
// осознанный выход из платформенного шаблона, что у BarkFluff.Navigator).
if (!isShellMode && !isProxyMode)
    builder.LoadConfiguration(ServiceId.Web);

// Env-переменные контейнера должны иметь приоритет над Configuration service
builder.Configuration.AddEnvironmentVariables();

// В Proxy-режиме адрес origin-ноды обязателен — fail fast, а не 502 на первом запросе.
if (isProxyMode && Uri.TryCreate(builder.Configuration["Web:Proxy:Target"], UriKind.Absolute, out var proxyTargetUri))
{
    if (proxyTargetUri.Scheme != Uri.UriSchemeHttp && proxyTargetUri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("Web:Proxy:Target должен быть http(s):// адресом Web-шлюза ноды");
}
else if (isProxyMode)
{
    throw new InvalidOperationException("Web:Mode=Proxy требует Web:Proxy:Target (публичный адрес Web-шлюза ноды, например https://web.barkfluff.com)");
}

// Allowlist файловых хостов ноды для media-relay: host → scheme. Запись может быть
// голым хостом (https) или 'http://host' для стендов без TLS. Чужие хосты relay
// не проксирует (403) — иначе Web превращается в open proxy.
var mediaHosts = isProxyMode
    ? ParseMediaHosts(builder.Configuration["Web:Proxy:MediaHosts"])
    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

// CORS — веб-клиент раздаётся с глобального web.barkfluff.com, а gRPC-Web ходит
// напрямую в ноду, поэтому origin вызывающего заранее неизвестен и список не ведём.
//
// AllowCredentials не нужен: токен идёт заголовком x-auth-token, куки в API не
// участвуют — значит CSRF нерелевантен, а AllowAnyOrigin допустим. API ноды и так
// публичный и защищён токеном.
//
// AllowAnyHeader вместо списка: он рассинхронизировался с клиентом (разрешался
// x-os, а метадата шлёт x-os-name) и same-origin это не выявлял — preflight'а не было.
//
// Preflight кэшируем на сутки, иначе каждый unary-вызов удваивает RTT.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .WithMethods("GET", "POST", "OPTIONS")
    .AllowAnyHeader()
    .SetPreflightMaxAge(TimeSpan.FromHours(24))
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

// Именованный HttpClient для media-relay (Proxy-режим).
const string MediaRelayClient = "media-relay";

// YARP reverse-proxy — по одному кластеру на каждый backend-сервис
// gRPC-Web middleware (ниже) превращает входящий HTTP/1.1 gRPC-Web запрос
// в обычный gRPC HTTP/2, после чего YARP форвардит его в соответствующий сервис.
// В Proxy-режиме один кластер remote уводит весь трафик на Web-шлюз origin-ноды.
builder.Services.AddReverseProxy()
    .LoadFromMemory(BuildRoutes(isShellMode, isProxyMode), BuildClusters(builder.Configuration, isShellMode, isProxyMode));

// Media-relay (только Proxy-режим): файловые хосты ноды (files.barkfluff.com и т.п.)
// недоступны из РФ напрямую, поэтому /media/{host}/... релеит их через себя.
// Timeout бесконечный: время жизни ответа = время жизни соединения клиента
// (большие видео на медленном канале); разрыв фиксирует отмена RequestAborted.
if (isProxyMode)
{
    builder.Services.AddHttpClient(MediaRelayClient, client => client.Timeout = Timeout.InfiniteTimeSpan)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Presigned-ссылки Minio могут отдавать редиректы — проксируем их как есть.
            AllowAutoRedirect = false
        });
}

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
    // Proxy-режим: grpc-web-text уходит на Web-шлюз ноды без изменений — base64
    // конвертацию делает удалённый шлюз, для Cloudflare это обычный HTTPS-запрос.
    if (isProxyMode)
    {
        await next();
        return;
    }

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

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/js/app/app-", StringComparison.Ordinal) && path.EndsWith(".js", StringComparison.Ordinal))
        {
            context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
        else if (path == "/js/app/app-manifest.json")
        {
            context.Context.Response.Headers.CacheControl = "no-store";
        }
        else
        {
            // Остальная статика раздаётся под фиксированными именами (proto- и vendor-бандлы,
            // css, словари i18n). Без заголовка браузер кэширует их по эвристике и может
            // держать старую версию часами. no-cache оставляет файл в кэше, но обязывает
            // проверять ETag — обновление приходит с ближайшим 200, а не с ручным бампом.
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});

app.MapHealthChecks("/health");
app.MapPingEndpoint();

// Firebase web configuration and the VAPID key are public client identifiers.
// Service-account credentials deliberately remain exclusive to CloudMessaging.
// В Proxy-режиме эндпоинт не маппится локально: catch-all маршрут уводит его на
// Web-шлюз ноды, и service worker прокси-домена получает конфигурацию origin-ноды.
if (!isProxyMode)
{
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
}

// Режим хоста для клиента: нода отдаёт себя как единственную (pinned),
// глобальный шелл заставляет выбрать ноду и проксирует Navigator,
// прокси-зеркало фиксирует ноду и помечается proxied (клиент оборачивает
// файловые ссылки в /media/relay на этом же хосте).
app.MapGet("/node-config.js", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    var script = "self.BF_NODE_CONFIG = " + JsonSerializer.Serialize(new
    {
        pinned = !isShellMode,
        navigatorProxy = isShellMode,
        proxied = isProxyMode
    }) + ";";
    return Results.Text(script, "application/javascript", Encoding.UTF8);
});

// Media-relay (только Proxy-режим): браузер не может грузить файлы ноды напрямую
// (файловые хосты за Cloudflare) — /media/{host}/{path}?{presigned-подпись} релеится
// через этот же Web. Range/If-Range пробрасываются, ответ стримится без буферизации.
if (isProxyMode)
{
    app.MapMethods("/media/{host}/{**path}", new[] { "GET", "HEAD" },
        async (string host, string path, HttpContext ctx, IHttpClientFactory httpClientFactory) =>
    {
        if (!mediaHosts.TryGetValue(host, out var scheme))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // path приходит из маршрута декодированным; кодируем каждый сегмент заново,
        // чтобы корректно пройти раундпроцент: '100%25.png' → '100%.png' → '100%25.png'.
        var encodedPath = string.Join("/", path.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
        var upstreamUri = $"{scheme}://{host}/{encodedPath}{ctx.Request.QueryString}";
        using var upstreamRequest = new HttpRequestMessage(
            HttpMethods.IsHead(ctx.Request.Method) ? HttpMethod.Head : HttpMethod.Get, upstreamUri);
        foreach (var header in new[] { "Range", "If-Range", "If-None-Match", "If-Modified-Since" })
        {
            if (ctx.Request.Headers.ContainsKey(header))
                upstreamRequest.Headers.TryAddWithoutValidation(header, ctx.Request.Headers[header].ToArray());
        }

        var client = httpClientFactory.CreateClient(MediaRelayClient);
        try
        {
            using var upstream = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);

            ctx.Response.StatusCode = (int)upstream.StatusCode;
            foreach (var header in new[] { "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified", "Cache-Control", "Expires" })
            {
                var values = upstream.Headers.TryGetValues(header, out var responseValues)
                    ? responseValues
                    : upstream.Content.Headers.TryGetValues(header, out var contentValues) ? contentValues : null;
                if (values is not null)
                    ctx.Response.Headers[header] = string.Join(", ", values);
            }
            // nginx спереди не должен буферизировать стриминг больших файлов
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            if (HttpMethods.IsHead(ctx.Request.Method))
                return;

            await upstream.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
        }
        catch (Exception) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Клиент оборвал загрузку — Kestrel прервёт ответ сам.
        }
        catch (HttpRequestException)
        {
            if (!ctx.Response.HasStarted)
                ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    });
}

app.MapGet("/messenger", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "messenger.html"), "text/html"));

app.MapReverseProxy();

// Fallback: SPA отдаётся для любого URL, КРОМЕ /api/*, /health и /ping —
// чтобы неверный HTTP-метод на upload/health/ping не маскировался index.html.
app.MapFallback(async (HttpContext ctx, IWebHostEnvironment env) =>
{
    var path = ctx.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/ping", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/ping/", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html").ExecuteAsync(ctx);
});

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();

// --- YARP routes / clusters ---

static IReadOnlyList<RouteConfig> BuildRoutes(bool isShellMode, bool isProxyMode)
{
    // Proxy-режим: один catch-all маршрут. Все пути (gRPC-сервисы, /api/files/upload,
    // /ping/{service}, /pwa-config.js и любые будущие эндпоинты) уходят на Web-шлюз
    // ноды без изменений — прокси не нужно править при появлении новых маршрутов.
    // Литеральные эндпоинты этого хоста (/health, /ping, /node-config.js, /media/*,
    // /messenger) выигрывают у catch-all по специфичности маршрута.
    if (isProxyMode)
    {
        return new List<RouteConfig>
        {
            new RouteConfig
            {
                RouteId = "remote-catchall",
                ClusterId = "remote",
                Match = new RouteMatch { Path = "/{**catchall}" }
            }
        };
    }

    // Полные имена gRPC-сервисов из *_api.proto (package + service).
    // Navigator — единственный маршрут, который есть и в shell-режиме: с него
    // начинается выбор ноды, а сам каталог живёт вне ноды.
    (string route, string cluster, string path)[] grpcRoutes = isShellMode
        ? new[]
        {
            ("navigator", "navigator", "/barkfluff.navigator.NavigatorApi/{**catchall}"),
        }
        : new[]
        {
            ("identity",  "identity",  "/barkfluff.identity.IdentityApi/{**catchall}"),
            ("users",     "users",     "/barkfluff.users.UsersApi/{**catchall}"),
            ("messages",  "messages",  "/barkfluff.messages.MessagesApi/{**catchall}"),
            ("files",     "files",     "/barkfluff.files.FilesApi/{**catchall}"),
            ("updates",   "updates",   "/barkfluff.updates.UpdatesApi/{**catchall}"),
            ("onliner",   "onliner",   "/barkfluff.onliner.OnlinerApi/{**catchall}"),
            ("fast-auth", "fast-auth", "/barkfluff.fast.auth.FastAuthApi/{**catchall}"),
            ("calls",     "calls",     "/barkfluff.calls.CallsApi/{**catchall}"),
            // Beacon — метаданные ноды (имя, цвета, livekit_url) после подключения.
            ("beacon",    "beacon",    "/barkfluff.beacon.BeaconApi/{**catchall}"),
            // Каталог нод доступен и с ноды: переключение сервера работает и там.
            ("navigator", "navigator", "/barkfluff.navigator.NavigatorApi/{**catchall}"),
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

    // Liveness checks use the same Web gateway as gRPC-Web. Backend services
    // expose an anonymous GET /ping, but their listeners are not public HTTP
    // endpoints from the browser's point of view.
    if (!isShellMode)
    {
        var pingRoutes = new[]
        {
            (name: "identity", cluster: "identity"),
            (name: "users", cluster: "users"),
            (name: "messages", cluster: "messages"),
            (name: "files", cluster: "files"),
            (name: "updates", cluster: "updates"),
            (name: "onliner", cluster: "onliner"),
            (name: "fast-auth", cluster: "fast-auth"),
            (name: "calls", cluster: "calls"),
            (name: "beacon", cluster: "beacon"),
            (name: "navigator", cluster: "navigator-health")
        };

        foreach (var (name, cluster) in pingRoutes)
        {
            routes.Add(new RouteConfig
            {
                RouteId = $"ping-{name}",
                ClusterId = cluster,
                Match = new RouteMatch
                {
                    Path = $"/ping/{name}",
                    Methods = new[] { "GET" }
                },
                Transforms = new[]
                {
                    new Dictionary<string, string> { { "PathSet", "/ping" } }
                }
            });
        }
    }

    // Шелл не проксирует файлы: аплоад идёт напрямую в ноду.
    if (isShellMode)
        return routes;

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

static IReadOnlyList<ClusterConfig> BuildClusters(IConfiguration config, bool isShellMode, bool isProxyMode)
{
    // Proxy-режим: единственный кластер на публичный Web-шлюз ноды (за Cloudflare).
    // HTTP/1.1 — входящий grpc-web-text и есть HTTP/1.1, конвертацию в HTTP/2 gRPC
    // делает удалённый шлюз; ActivityTimeout 24ч — долгоживущие server-streaming
    // подписки (updates/onliner/fast-auth/calls) молчат часами.
    if (isProxyMode)
    {
        var proxyTarget = config["Web:Proxy:Target"]!;
        return new List<ClusterConfig>
        {
            new ClusterConfig
            {
                ClusterId = "remote",
                HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
                {
                    Version = new Version(1, 1),
                    VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower,
                    ActivityTimeout = TimeSpan.FromHours(24)
                },
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["remote-1"] = new DestinationConfig { Address = proxyTarget }
                }
            }
        };
    }

    // Публичный каталог нод — единственный внешний адрес, который знает и шелл, и нода.
    var navigatorCluster = ("navigator", "NavigatorService:Host", "https://navigator.barkfluff.com:443");

    // HTTP/2 (h2c) cluster для gRPC-вызовов — сервисы слушают gRPC на основном порту.
    (string clusterId, string configKey, string defaultHost)[] grpcClusters = isShellMode
        ? new[] { navigatorCluster }
        : new[]
    {
        ("identity",  "IdentityService:Host",  "http://identity:7000"),
        ("users",     "UsersService:Host",     "http://users:7001"),
        ("messages",  "MessagesService:Host",  "http://messages:7007"),
        ("files",     "FilesService:Host",     "http://files:7005"),
        ("updates",   "UpdatesService:Host",   "http://updates:7015"),
        ("onliner",   "OnlinerService:Host",   "http://onliner:7009"),
        ("fast-auth", "FastAuthService:Host",  "http://fast-auth:7008"),
        ("calls",     "CallsService:Host",     "http://calls:7025"),
        ("beacon",    "BeaconService:Host",    "http://beacon:7002"),
        navigatorCluster,
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

    if (!isShellMode)
    {
        // Navigator is deliberately configured with its public TLS endpoint
        // for gRPC discovery. Its liveness request must use the internal
        // HTTP/2 listener because the public nginx route is gRPC-only.
        var navigatorHealthHost = config["NavigatorService:HealthHost"] ?? "http://navigator:7010";
        clusters.Add(new ClusterConfig
        {
            ClusterId = "navigator-health",
            HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
            {
                Version = new Version(2, 0),
                VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
                ActivityTimeout = TimeSpan.FromSeconds(10)
            },
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["navigator-health-1"] = new DestinationConfig { Address = navigatorHealthHost }
            }
        });
    }

    if (isShellMode)
        return clusters;

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
/// Разбирает Web:Proxy:MediaHosts в host → scheme. Запись — хост (https по
/// умолчанию) или явный 'http://host' / 'https://host' для стендов без TLS.
/// </summary>
static Dictionary<string, string> ParseMediaHosts(string? raw)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in (raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            result[entry["https://".Length..]] = "https";
        else if (entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            result[entry["http://".Length..]] = "http";
        else
            result[entry] = "https";
    }
    return result;
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
