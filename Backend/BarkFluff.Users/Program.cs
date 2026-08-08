using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Consumers;
using BarkFluff.Users.Host;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Users;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Users);
        builder.AddBarkFluffSerilog("BarkFluff.Users");
        builder.SetRunningAddress(builder.Configuration);

        // Регистрируем gRPC сервисы с интерцепторами
        builder.Services.AddBarkFluffGrpc();
        builder.Services.AddBarkFluffMetrics("BarkFluff.Users");

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<UsersContext>(c
            => c.UseNpgsql(builder.Configuration["UsersDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddTransient<UsersStorage>();
        builder.Services.AddTransient<DevicesStorage>();
        builder.Services.AddTransient<PrivacyStorage>();
        builder.Services.AddTransient<PersonalizationStorage>();
        builder.Services.AddTransient<UserSettingsStorage>();
        builder.Services.AddTransient<ChatFolderStorage>();
        builder.Services.AddTransient<ChatMuteStorage>();
        builder.Services.AddTransient<PrekeyStorage>();
        builder.Services.AddTransient<RemoteUsersStorage>();
        builder.Services.AddScoped<UserInfoQueueSender>();
        builder.Services.AddSingleton<ReservedUsernamesService>();

        // Регистрируем аутентификацию и авторизацию
        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddGrpcClient<FilesServerApi.FilesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["FilesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FilesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddGrpcClient<MessagesServerApi.MessagesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["MessagesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["MessagesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        // gRPC-клиент к Federation: ResolveRemoteUser (этап 2.1).
        builder.Services.AddGrpcClient<FederationInternalApi.FederationInternalApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["FederationService:Host"] ?? "http://federation:7030");
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FederationService:Token"] ?? string.Empty))
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

                cfg.ReceiveEndpoint($"session-revoked-users-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });
            });
        });

        var app = builder.Build();

        // Гейджи: время старта и health-флаг миграции (0 — не применена / упала, 1 — успех).
        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        startupMetrics.Set("db_migration_healthy", 0);

        // Применение миграций базы данных
        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<UsersContext>();
            ctx.Database.Migrate();
            startupMetrics.Set("db_migration_healthy", 1);
        }

        app.MapGrpcReflectionService();

        // Настраиваем middleware pipeline
        app.UseRouting();

        app.UseXAuth();
        app.MapPingEndpoint();

        // Регистрируем gRPC сервисы
        app.MapGrpcService<UsersServerApiService>();
        app.MapGrpcService<UsersApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
