using BarkFluff.Bots.Consumers;
using BarkFluff.Bots.Host;
using BarkFluff.Bots.Host.Http;
using BarkFluff.Bots.Infrastructure;
using BarkFluff.Bots.Persistence;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Bots.Services.BotFather;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using StackExchange.Redis;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Bots;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Bots);
        builder.AddBarkFluffSerilog("BarkFluff.Bots");
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        }).AddServiceOptions<BotsExternalApiService>(options =>
        {
            // Сверка token-id + rate-limit только для внешнего Bot API (после XAuth)
            options.Interceptors.Add<BotAuthInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Bots");

        if (builder.Environment.IsDevelopment())
            builder.Services.AddGrpcReflection();

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddDbContext<BotsContext>(c
            => c.UseNpgsql(builder.Configuration["BotsDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddScoped<BotsStorage>();
        builder.Services.AddScoped<BotUpdatesStorage>();
        builder.Services.AddScoped<BotFatherSessionsStorage>();
        builder.Services.AddScoped<BotFatherService>();
        builder.Services.AddScoped<SystemBotsSeeder>();
        builder.Services.AddHttpClient();
        // Redis — общий rate-limit и распределённый polling-guard (масштабирование, см. bots.md).
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(builder.Configuration["Redis"]
                ?? throw new InvalidOperationException("Redis configuration is missing")));
        builder.Services.AddSingleton<BotRegistryCache>();
        builder.Services.AddSingleton<BotUpdateNotifier>();
        builder.Services.AddSingleton<IBotRateLimiter, RedisBotRateLimiter>();
        builder.Services.AddSingleton<IBotPollingGuard, RedisBotPollingGuard>();
        builder.Services.AddSingleton<BotAccessValidator>();
        builder.Services.AddScoped<BotCallerContext>();
        builder.Services.AddScoped<BotTokenIssuer>();
        builder.Services.AddHostedService<BotsCleanupService>();

        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["UsersService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddGrpcClient<MessagesServerApi.MessagesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["MessagesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["MessagesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddGrpcClient<FilesServerApi.FilesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["FilesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FilesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddGrpcClient<IdentityServerApi.IdentityServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["IdentityService:Host"] ?? "http://identity:7000");
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["IdentityService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<NewMessageConsumer>();
            x.AddConsumer<LoginNotificationConsumer>();
            x.AddConsumer<BotUpdateSignalConsumer>();
            x.AddConsumer<BotRegistryChangedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint("new-messages-bots-handler", e =>
                {
                    e.ConfigureConsumer<NewMessageConsumer>(context);
                });

                cfg.ReceiveEndpoint("email-notifications-bots-handler", e =>
                {
                    e.ConfigureConsumer<LoginNotificationConsumer>(context);
                });

                // Fan-out: каждый инстанс будит своих локальных poll/стрим-waiter'ов и инвалидирует
                // свой кэш реестра (уникальная очередь на инстанс, см. bots.md).
                cfg.ReceiveEndpoint($"bot-update-signal-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<BotUpdateSignalConsumer>(context);
                });

                cfg.ReceiveEndpoint($"bot-registry-changed-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<BotRegistryChangedConsumer>(context);
                });
            });
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BotsContext>();
            ctx.Database.Migrate();

            var seeder = scope.ServiceProvider.GetRequiredService<SystemBotsSeeder>();
            seeder.SeedAsync().GetAwaiter().GetResult();
        }

        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();

        app.UseRouting();

        app.UseXAuth();

        app.MapGrpcService<BotsServerApiService>();
        app.MapGrpcService<BotsExternalApiService>();

        // Bot REST API (HTTP/1.1 на RunSettings:Http1Port)
        app.MapBotApiEndpoints();

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
