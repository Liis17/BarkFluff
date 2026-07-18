using BarkFluff.Federation.BackgroundServices;
using BarkFluff.Federation.Host;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Navigator;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Federation;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Federation);
        builder.AddBarkFluffSerilog("BarkFluff.Federation");
        builder.SetRunningAddress(builder.Configuration);

        // Второй листенер HTTP/1 для /.well-known/barkfluff — gRPC-порт настроен под h2c (SetRunningAddress),
        // GET по HTTP/1 на нём не живёт. Дефолт порта — 7031 (см. ConfigurationDefaultsPopulator: свободен).
        var wellKnownPortRaw = builder.Configuration["Federation:WellKnownPort"];
        var wellKnownPort = !string.IsNullOrWhiteSpace(wellKnownPortRaw) && int.TryParse(wellKnownPortRaw, out var parsedWellKnownPort)
            ? parsedWellKnownPort
            : 7031;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(wellKnownPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
        });

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        }).AddServiceOptions<FederationS2SApiService>(options =>
        {
            // XFed — не XAuth; проверка подписи только для FederationS2SApi (см. XFedServerInterceptor).
            options.Interceptors.Add<XFedServerInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Federation");

        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<FederationContext>(c
            => c.UseNpgsql(builder.Configuration["FederationDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddScoped<SigningKeyService>();
        builder.Services.AddSingleton<WellKnownDocumentService>();
        builder.Services.AddSingleton<ActiveSigningKeyCache>();
        builder.Services.AddSingleton<S2SChannelFactory>();

        builder.Services.AddSingleton<ServernameValidator>();
        builder.Services.AddSingleton<IWellKnownClient, WellKnownClient>();
        builder.Services.AddScoped<INavigatorClient, NavigatorClient>();
        builder.Services.AddScoped<ServerResolver>();
        builder.Services.AddSingleton<DiscoveryTriggerRateLimiter>();
        builder.Services.AddHostedService<PeerRefreshBackgroundService>();

        builder.Services.AddGrpcClient<NavigatorApi.NavigatorApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["NavigatorUrl"]!);
        });

        builder.Services.AddXAuth(builder.Configuration);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<FederationContext>();
            ctx.Database.Migrate();

            var signingKeyService = scope.ServiceProvider.GetRequiredService<SigningKeyService>();
            signingKeyService.EnsureActiveKeyAsync().GetAwaiter().GetResult();

            var wellKnownDocumentService = scope.ServiceProvider.GetRequiredService<WellKnownDocumentService>();
            wellKnownDocumentService.RebuildAsync().GetAwaiter().GetResult();

            var activeSigningKeyCache = scope.ServiceProvider.GetRequiredService<ActiveSigningKeyCache>();
            activeSigningKeyCache.RefreshAsync().GetAwaiter().GetResult();
        }

        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();

        app.UseRouting();

        app.UseXAuth();

        app.UseMiddleware<XFedRawBytesMiddleware>();

        app.MapGrpcService<FederationS2SApiService>();
        app.MapGrpcService<FederationInternalApiService>();

        app.MapGet("/.well-known/barkfluff", (WellKnownDocumentService wellKnownDocumentService) =>
        {
            var document = wellKnownDocumentService.GetCachedDocument();
            if (document == null)
                return Results.Json(new { error = "federation not configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);

            return Results.Content(document, "application/json");
        });

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
