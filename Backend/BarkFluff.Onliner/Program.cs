using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Consumers;
using BarkFluff.Onliner.Features.SubscribeToOnlineStatus;
using BarkFluff.Onliner.Features.SubscribeToTyping;
using BarkFluff.Onliner.Host;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Serilog;

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

        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<OnlineStatusContext>(c
            => c.UseNpgsql(builder.Configuration["OnlinerDb"]));

        // Регистрируем все Onliner сервисы (Storage, Notifier, Background Services, MediatR)
        builder.Services.AddOnlinerServices();

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

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<SessionRevokedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint("session-revoked-onliner", e =>
                {
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });
            });
        });

        var app = builder.Build();

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();
            ctx.Database.Migrate();
        }

        app.MapGrpcReflectionService();

        // Настраиваем middleware pipeline
        app.UseRouting();

        app.UseXAuth();

        // Регистрируем gRPC сервисы
        app.MapGrpcService<OnlinerApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}