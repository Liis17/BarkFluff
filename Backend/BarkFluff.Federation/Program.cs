using BarkFluff.Federation.BackgroundServices;
using BarkFluff.Federation.Consumers;
using BarkFluff.Federation.Features.FederationInternalApi;
using BarkFluff.Federation.Features.FederationS2SApi;
using BarkFluff.Federation.Host;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Navigator;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Onliner;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

using Serilog;

using StackExchange.Redis;

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

        if (builder.Environment.IsDevelopment())
            builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<FederationContext>(c
            => c.UseNpgsql(builder.Configuration["FederationDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddScoped<FederationInternalApiHandler>();
        builder.Services.AddScoped<FederationS2SApiHandler>();

        builder.Services.AddScoped<SigningKeyService>();
        builder.Services.AddSingleton<WellKnownDocumentService>();
        builder.Services.AddSingleton<ActiveSigningKeyCache>();
        builder.Services.AddSingleton<S2SChannelFactory>();
        builder.Services.AddSingleton<IS2SChannelInvalidator>(sp => sp.GetRequiredService<S2SChannelFactory>());

        builder.Services.AddSingleton<FederationSwitch>();
        builder.Services.AddSingleton<ServernameValidator>();
        builder.Services.AddSingleton<IWellKnownClient, WellKnownClient>();
        builder.Services.AddScoped<INavigatorClient, NavigatorClient>();
        builder.Services.AddScoped<ServerResolver>();
        builder.Services.AddSingleton<IDiscoveryTriggerRateLimiter, RedisDiscoveryTriggerRateLimiter>();
        builder.Services.AddHostedService<PeerRefreshBackgroundService>();

        // Redis (этап 2.5) — квота ChatCreated per-origin (ChatCreatedQuotaLimiter).
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(builder.Configuration["Redis"]
                ?? throw new InvalidOperationException("Redis configuration is missing")));
        builder.Services.AddSingleton<IChatCreatedQuotaLimiter, ChatCreatedQuotaLimiter>();
        // Single-runner для фоновых задач (масштабирование, docs/scaling/federation.md).
        builder.Services.AddSingleton<ISingleRunner, RedisSingleRunner>();

        // Outbox (этап 2.2): writer + диспетчер + janitor.
        builder.Services.AddScoped<OutboxWriter>();
        builder.Services.AddHostedService<OutboxDispatcher>();
        builder.Services.AddHostedService<OutboxJanitor>();

        // Presence-мост (этап 4.3). Ничего из этого не персистится: presence эфемерен —
        // состояние восстанавливается heartbeat'ами интереса и реконнектом стримов.
        builder.Services.AddSingleton<PresenceOptions>();
        builder.Services.AddSingleton<PresenceInterestRegistry>();
        builder.Services.AddSingleton<IncomingPresenceRegistry>();
        builder.Services.AddSingleton<RemoteUserServerCache>();
        builder.Services.AddSingleton<PeerCapabilityCache>();
        builder.Services.AddHostedService<PresenceStreamManager>();

        // Typing-мост (этап 4.4): coalescing на исходящем пути, лимит и кеш валидации — на входящем.
        // Скачивание federated-файлов (этап 3.2).
        builder.Services.AddSingleton<FederatedFileOptions>();
        builder.Services.AddSingleton<IFetchFileRateLimiter, FetchFileRateLimiter>();
        // Circuit breaker скачивания per-origin (этап 3.5) — in-memory, per-instance.
        builder.Services.AddSingleton<RemoteFileCircuitBreaker>();

        builder.Services.AddSingleton<TypingCoalescer>();
        builder.Services.AddSingleton<TypingValidationCache>();
        builder.Services.AddSingleton<ITypingRateLimiter, TypingRateLimiter>();

        builder.Services.AddGrpcClient<NavigatorApi.NavigatorApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["NavigatorUrl"]!);
        });

        // gRPC-клиент к Users: GetFederatedProfile (этап 2.1, S2S GetUserProfile).
        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["UsersService:Host"]!);
        }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"] ?? string.Empty))
          .AddInterceptor(() => new ExceptionClientInterceptor());

        // gRPC-клиент к Messages: ImportFederatedChat/ImportFederatedMessage (этап 2.3, S2S DeliverEvents routing).
        builder.Services.AddGrpcClient<MessagesServerApi.MessagesServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["MessagesService:Host"]!);
        }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["MessagesService:Token"] ?? string.Empty))
          .AddInterceptor(() => new ExceptionClientInterceptor());

        // gRPC-клиент к Files: FetchFileStream — отдача содержимого файла ноде-партнёру (этап 3.2).
        builder.Services.AddGrpcClient<FilesServerApi.FilesServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["FilesService:Host"]!);
        }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FilesService:Token"] ?? string.Empty))
          .AddInterceptor(() => new ExceptionClientInterceptor());

        // gRPC-клиент к Onliner: GetLocalPresence (отдача статусов партнёру) и
        // UpsertRemoteStatus (вливание полученных), этап 4.3.
        builder.Services.AddGrpcClient<OnlinerServerApi.OnlinerServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["OnlinerService:Host"]!);
        }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["OnlinerService:Token"] ?? string.Empty))
          .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddMassTransit(x =>
        {
            // Консюмеры внутренних событий → FederationOutbox (этап 2.2).
            x.AddConsumer<NewMessageFederationConsumer>();
            // Изменения статусов локальных пользователей → входящие presence-стримы (этап 4.3).
            x.AddConsumer<PresenceStatusChangedConsumer>();
            x.AddConsumer<MessageEditedFederationConsumer>();
            x.AddConsumer<MessageDeletedFederationConsumer>();
            x.AddConsumer<MessageReadFederationConsumer>();
            x.AddConsumer<SessionRevokedConsumer>();
            x.AddConsumer<SigningKeyRotatedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint("new-messages-federation-handler", e => e.ConfigureConsumer<NewMessageFederationConsumer>(context));
                cfg.ReceiveEndpoint("messages-edited-federation-handler", e => e.ConfigureConsumer<MessageEditedFederationConsumer>(context));
                cfg.ReceiveEndpoint("messages-deleted-federation-handler", e => e.ConfigureConsumer<MessageDeletedFederationConsumer>(context));
                cfg.ReceiveEndpoint("read-receipts-federation-handler", e => e.ConfigureConsumer<MessageReadFederationConsumer>(context));
                cfg.ReceiveEndpoint($"session-revoked-federation-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });

                // Fan-out: очередь на экземпляр — входящие presence-стримы живут в памяти
                // конкретного процесса, и события статусов нужны каждому.
                cfg.ReceiveEndpoint($"presence-status-changed-federation-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<PresenceStatusChangedConsumer>(context);
                });

                // Fan-out: каждый инстанс перезагружает кэш активного ключа и well-known при ротации.
                cfg.ReceiveEndpoint($"signing-key-rotated-federation-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<SigningKeyRotatedConsumer>(context);
                });
            });
        });

        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddBarkFluffHealth();

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
        app.MapHealthEndpoints();

        app.MapGet("/.well-known/barkfluff", (WellKnownDocumentService wellKnownDocumentService, FederationSwitch federationSwitch) =>
        {
            // P1-04: выключенная/несконфигурированная нода не публикует well-known.
            if (!federationSwitch.IsActive)
                return Results.Json(new { error = "federation not configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);

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
