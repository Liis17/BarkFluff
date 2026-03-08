using BarkFluff.GrpcServer;
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
    });
});

var app = builder.Build();
app.MapGrpcReflectionService();
app.UseRouting();

app.UseXAuth();

app.MapGrpcService<UpdatesApiService>();

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();