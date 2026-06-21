using BarkFluff.Calls.Consumers;
using BarkFluff.Calls.Host;
using BarkFluff.Calls.Persistence;
using BarkFluff.Calls.Services;
using BarkFluff.Calls.Settings;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using Livekit.Server.Sdk.Dotnet;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Calls;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Calls);
        builder.AddBarkFluffSerilog("BarkFluff.Calls");
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Calls");
        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<CallsContext>(c
            => c.UseNpgsql(builder.Configuration["CallsDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddSettings<LiveKitSettings>(builder.Configuration, "LiveKit");
        builder.Services.AddSingleton<LiveKitTokenService>();
        builder.Services.AddSingleton<CallEventSubscriptionsManager>();
        builder.Services.AddSingleton<CallTimeoutScheduler>();
        builder.Services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<LiveKitSettings>();
            return new WebhookReceiver(settings.ApiKey, settings.ApiSecret);
        });
        builder.Services.AddScoped<CallsService>();

        builder.Services.AddXAuth(builder.Configuration);

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

                cfg.ReceiveEndpoint("session-revoked-calls", e =>
                {
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });
            });
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<CallsContext>();
            ctx.Database.Migrate();
        }

        app.MapGrpcReflectionService();
        app.UseRouting();

        app.UseXAuth();

        app.MapGrpcService<CallsApiService>();

        // LiveKit webhooks (HTTP/1.1 на RunSettings:Http1Port) — финализация CDR и participant-события.
        app.MapPost("/livekit/webhook", async (
            HttpRequest request,
            WebhookReceiver receiver,
            CallsService calls,
            ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var authHeader = request.Headers["Authorization"].FirstOrDefault();

            WebhookEvent webhookEvent;
            try
            {
                webhookEvent = receiver.Receive(body, authHeader);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LiveKit webhook: верификация подписи не прошла");
                return Results.Unauthorized();
            }

            switch (webhookEvent.Event)
            {
                case "room_finished":
                    await calls.HandleRoomFinishedAsync(webhookEvent.Room?.Name ?? string.Empty);
                    break;
                case "participant_joined":
                    await calls.HandleParticipantAsync(
                        webhookEvent.Room?.Name ?? string.Empty,
                        webhookEvent.Participant?.Identity ?? string.Empty, joined: true);
                    break;
                case "participant_left":
                    await calls.HandleParticipantAsync(
                        webhookEvent.Room?.Name ?? string.Empty,
                        webhookEvent.Participant?.Identity ?? string.Empty, joined: false);
                    break;
            }

            return Results.Ok();
        }).AllowAnonymous();

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        startupMetrics.Set("call_events_subscriptions_active", 0);

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
