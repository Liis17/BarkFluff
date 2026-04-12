using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

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

// gRPC-Web: transparently переупаковывает входящий HTTP/1.1 gRPC-Web в gRPC,
// чтобы YARP мог форвардить как HTTP/2 gRPC во внутреннюю сеть.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

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
