using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using BarkFluff.Updates;
using BarkFluff.Updates.Consumers;
using BarkFluff.Updates.Host;

using MassTransit;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.LoadConfiguration(ServiceId.Updates);
builder.AddBarkFluffSerilog("BarkFluff.Updates");
builder.SetRunningAddress(builder.Configuration);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<ServerExceptionInterceptor>();
});
builder.Services.AddBarkFluffMetrics("BarkFluff.Updates");

builder.Services.AddGrpcReflection();

// Register Updates services including StreamSubscriptionsManager as Singleton
builder.Services.AddUpdatesServices();

builder.Services.AddXAuth(builder.Configuration);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<NewMessageConsumer>();
    x.AddConsumer<ReadByConsumer>();
    x.AddConsumer<SessionRevokedConsumer>();
    x.AddConsumer<MessageEditedConsumer>();
    x.AddConsumer<MessageDeletedConsumer>();
    x.AddConsumer<MessagePinnedConsumer>();
    x.AddConsumer<MessageUnpinnedConsumer>();
    x.AddConsumer<AllMessagesUnpinnedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });

        cfg.ReceiveEndpoint("new-messages-updates-handler", e =>
        {
            e.ConfigureConsumer<NewMessageConsumer>(context);
        });

        cfg.ReceiveEndpoint("read-receipts-updates-handler", e =>
        {
            e.ConfigureConsumer<ReadByConsumer>(context);
        });

        cfg.ReceiveEndpoint("session-revoked-updates", e =>
        {
            e.ConfigureConsumer<SessionRevokedConsumer>(context);
        });

        cfg.ReceiveEndpoint("messages-edited-updates-handler", e =>
        {
            e.ConfigureConsumer<MessageEditedConsumer>(context);
        });

        cfg.ReceiveEndpoint("messages-deleted-updates-handler", e =>
        {
            e.ConfigureConsumer<MessageDeletedConsumer>(context);
        });

        cfg.ReceiveEndpoint("messages-pinned-updates-handler", e =>
        {
            e.ConfigureConsumer<MessagePinnedConsumer>(context);
        });

        cfg.ReceiveEndpoint("messages-unpinned-updates-handler", e =>
        {
            e.ConfigureConsumer<MessageUnpinnedConsumer>(context);
        });

        cfg.ReceiveEndpoint("all-messages-unpinned-updates-handler", e =>
        {
            e.ConfigureConsumer<AllMessagesUnpinnedConsumer>(context);
        });
    });
});

var app = builder.Build();
app.MapGrpcReflectionService();
app.UseRouting();

app.UseXAuth();

app.MapGrpcService<UpdatesApiService>();

// Стартовые gauges для метрик
var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
startupMetrics.Set("new_messages_subscriptions_active", 0);
startupMetrics.Set("read_by_subscriptions_active", 0);
startupMetrics.Set("messages_edited_subscriptions_active", 0);
startupMetrics.Set("messages_deleted_subscriptions_active", 0);
startupMetrics.Set("messages_pinned_subscriptions_active", 0);
startupMetrics.Set("messages_unpinned_subscriptions_active", 0);
startupMetrics.Set("all_messages_unpinned_subscriptions_active", 0);
startupMetrics.Set("subscriptions_active_total", 0);

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();