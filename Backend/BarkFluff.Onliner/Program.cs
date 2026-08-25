using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Consumers;
using BarkFluff.Onliner.Features.SubscribeToOnlineStatus;
using BarkFluff.Onliner.Features.SubscribeToTyping;
using BarkFluff.Onliner.Host;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Serilog;

using StackExchange.Redis;

namespace BarkFluff.Onliner;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Onliner);
        builder.AddBarkFluffSerilog("BarkFluff.Onliner");
        builder.SetRunningAddress(builder.Configuration);

        // Регистрируем gRPC сервисы с интерцепторами
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Onliner");

        if (builder.Environment.IsDevelopment())
            builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<OnlineStatusContext>(c
            => c.UseNpgsql(builder.Configuration["OnlinerDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        // Redis — общий presence-стор и распределённый single-runner (масштабирование, см. onliner.md).
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(builder.Configuration["Redis"]
                ?? throw new InvalidOperationException("Redis configuration is missing")));

        // Регистрируем все Onliner сервисы (Presence, Notifier, Background Services, MediatR)
        builder.Services.AddOnlinerServices(builder.Configuration);

        // Регистрируем handler для streaming (не через MediatR)
        builder.Services.AddScoped<SubscribeToOnlineStatusQueryHandler>();
        builder.Services.AddScoped<SubscribeToTypingQueryHandler>();

        // Регистрируем аутентификацию и авторизацию
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

        // Клиент Federation (этап 4.2): интерес к remote-presence + исходящий typing (этап 4.4).
        // Ключи живут в бакете ПОТРЕБИТЕЛЯ — ServiceId.Onliner = 9. Регистрируем только когда
        // хост задан: нода без федерации не должна падать на старте.
        var federationHost = builder.Configuration["FederationService:Host"];
        if (!string.IsNullOrWhiteSpace(federationHost))
        {
            builder.Services.AddGrpcClient<FederationInternalApi.FederationInternalApiClient>(o =>
                {
                    o.Address = new Uri(federationHost);
                }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FederationService:Token"]))
                .AddInterceptor(() => new ExceptionClientInterceptor());
        }

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<SessionRevokedConsumer>();
            x.AddConsumer<OnlineStatusChangedConsumer>();
            x.AddConsumer<TypingChangedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint($"session-revoked-onliner-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });

                // Fan-out: каждый инстанс получает копию изменения статуса/набора и доставляет
                // своим локальным gRPC-подпискам (стрим подписчика живёт на одном инстансе).
                cfg.ReceiveEndpoint($"online-status-changed-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<OnlineStatusChangedConsumer>(context);
                });

                cfg.ReceiveEndpoint($"typing-changed-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<TypingChangedConsumer>(context);
                });
            });
        });

        builder.Services.AddBarkFluffHealth();

        var app = builder.Build();

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();
            ctx.Database.Migrate();
        }

        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();

        // Настраиваем middleware pipeline
        app.UseRouting();

        app.UseXAuth();
        app.MapHealthEndpoints();

        // Регистрируем gRPC сервисы
        app.MapGrpcService<OnlinerApiService>();
        app.MapGrpcService<OnlinerServerApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
