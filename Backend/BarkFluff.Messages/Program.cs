using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Host;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Infrastructure.Behaviors;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Serilog;

using StackExchange.Redis;

namespace BarkFluff.Messages;

using Consumers;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Messages);
        builder.AddBarkFluffSerilog("BarkFluff.Messages");
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Messages");
        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<MessagesContext>(c
            => c.UseNpgsql(builder.Configuration["MessagesDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = builder.Configuration["Redis"];
            options.InstanceName = "Messages_";
        });

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(builder.Configuration["Redis"]
                ?? throw new InvalidOperationException("Redis configuration is missing")));

        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.AddOpenBehavior(typeof(MetricsBehavior<,>));
        });

        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["UsersService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddGrpcClient<FilesServerApi.FilesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["FilesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FilesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddTransient<ChatsStorage>();
        builder.Services.AddScoped<ChatCache>();
        builder.Services.AddTransient<MessagesStorage>();
        builder.Services.AddTransient<PinnedMessagesStorage>();
        builder.Services.AddTransient<EncryptedMessagesStorage>();
        builder.Services.AddTransient<FederatedReadStatesStorage>();
        builder.Services.AddTransient<ChatDraftsStorage>();
        builder.Services.AddTransient<Mapping.ReplyPreviewResolver>();
        builder.Services.AddSingleton<SecretMessageBuffer>();
        builder.Services.AddSingleton<PrivateChatInviteStore>();
        builder.Services.AddTransient<MessageQueueSender>();
        builder.Services.AddTransient<EncryptedMessageQueueSender>();
        builder.Services.AddTransient<SecretMessageQueueSender>();
        builder.Services.AddTransient<ReadByQueueSender>();

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<UserChangedAvatarConsumer>();
            x.AddConsumer<UserChangedNameConsumer>();
            x.AddConsumer<SessionRevokedConsumer>();
            x.AddConsumer<FederatedChatRejectedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint("user-changed-name-messages", e =>
                {
                    e.ConfigureConsumer<UserChangedNameConsumer>(context);
                });

                cfg.ReceiveEndpoint("user-changed-avatar-messages", e =>
                {
                    e.ConfigureConsumer<UserChangedAvatarConsumer>(context);
                });

                cfg.ReceiveEndpoint($"session-revoked-messages-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });

                cfg.ReceiveEndpoint("federated-chat-rejected-messages", e =>
                {
                    e.ConfigureConsumer<FederatedChatRejectedConsumer>(context);
                });
            });
        });

        builder.Services.AddBarkFluffHealth();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MessagesContext>();
            ctx.Database.Migrate();
        }

        app.MapGrpcReflectionService();
        app.UseRouting();

        app.UseXAuth();
        app.MapHealthEndpoints();

        app.MapGrpcService<MessagesApiService>();
        app.MapGrpcService<MessagesServerApiService>();

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
