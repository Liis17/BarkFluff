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

if (builder.Environment.IsDevelopment())
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
    x.AddConsumer<NewEncryptedMessageConsumer>();
    x.AddConsumer<EncryptedMessageEditedConsumer>();
    x.AddConsumer<EncryptedMessageDeletedConsumer>();
    x.AddConsumer<PrivateMessagesReadConsumer>();
    x.AddConsumer<PrivateChatInviteConsumer>();
    x.AddConsumer<PrivateChatInviteResolutionConsumer>();
    x.AddConsumer<SecretChatInviteConsumer>();
    x.AddConsumer<SecretChatInviteResolutionConsumer>();
    x.AddConsumer<NewSecretMessageConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });

        // Все стрим-эндпоинты — fan-out: уникальная очередь на инстанс, чтобы каждый инстанс получал
        // копию события и доставлял её своим локальным подписчикам gRPC-стримов (см. updates.md).
        cfg.ReceiveEndpoint($"new-messages-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<NewMessageConsumer>(context);
        });

        cfg.ReceiveEndpoint($"read-receipts-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<ReadByConsumer>(context);
        });

        cfg.ReceiveEndpoint($"session-revoked-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<SessionRevokedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"messages-edited-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<MessageEditedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"messages-deleted-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<MessageDeletedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"messages-pinned-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<MessagePinnedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"messages-unpinned-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<MessageUnpinnedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"all-messages-unpinned-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<AllMessagesUnpinnedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"new-encrypted-messages-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<NewEncryptedMessageConsumer>(context);
        });

        cfg.ReceiveEndpoint($"encrypted-messages-edited-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<EncryptedMessageEditedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"encrypted-messages-deleted-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<EncryptedMessageDeletedConsumer>(context);
        });

        cfg.ReceiveEndpoint($"private-messages-read-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<PrivateMessagesReadConsumer>(context);
        });

        cfg.ReceiveEndpoint($"private-chat-invites-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<PrivateChatInviteConsumer>(context);
        });

        cfg.ReceiveEndpoint($"private-chat-invite-resolutions-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<PrivateChatInviteResolutionConsumer>(context);
        });

        cfg.ReceiveEndpoint($"secret-chat-invites-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<SecretChatInviteConsumer>(context);
        });

        cfg.ReceiveEndpoint($"secret-chat-invite-resolutions-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<SecretChatInviteResolutionConsumer>(context);
        });

        cfg.ReceiveEndpoint($"new-secret-messages-updates-{InstanceId.Current}", e =>
        {
            e.AutoDelete = true;
            e.Durable = false;
            e.ConfigureConsumer<NewSecretMessageConsumer>(context);
        });
    });
});

builder.Services.AddBarkFluffHealth();

var app = builder.Build();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.UseRouting();

app.UseXAuth();
app.MapHealthEndpoints();

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
startupMetrics.Set("private_messages_subscriptions_active", 0);
startupMetrics.Set("private_message_edits_subscriptions_active", 0);
startupMetrics.Set("private_message_deletes_subscriptions_active", 0);
startupMetrics.Set("private_messages_read_subscriptions_active", 0);
startupMetrics.Set("private_chat_invites_subscriptions_active", 0);
startupMetrics.Set("private_chat_invite_resolutions_subscriptions_active", 0);
startupMetrics.Set("secret_chat_invites_subscriptions_active", 0);
startupMetrics.Set("secret_chat_resolutions_subscriptions_active", 0);
startupMetrics.Set("secret_messages_subscriptions_active", 0);
startupMetrics.Set("subscriptions_active_total", 0);

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();
