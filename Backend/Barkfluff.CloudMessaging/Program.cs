using Barkfluff.CloudMessaging.Consumers;
using Barkfluff.CloudMessaging.Services;

using BarkFluff.GrpcServer;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.LoadConfiguration(ServiceId.CloudMessaging);
builder.AddBarkFluffSerilog("BarkFluff.CloudMessaging");
builder.SetRunningAddress(builder.Configuration);

// Firebase service
builder.Services.AddSingleton<FirebaseService>();

// gRPC клиент к Users сервису
builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
    {
        o.Address = new Uri(builder.Configuration["UsersService:Host"] ?? throw new InvalidOperationException("UsersService:Host not configured"));
    })
    .AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"] ?? throw new InvalidOperationException("UsersService:Token not configured")))
    .AddInterceptor(() => new ExceptionClientInterceptor());

// gRPC клиент к Messages сервису
builder.Services.AddGrpcClient<MessagesApi.MessagesApiClient>(o =>
    {
        o.Address = new Uri(builder.Configuration["MessagesService:Host"] ?? throw new InvalidOperationException("MessagesService:Host not configured"));
    })
    .AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["MessagesService:Token"] ?? throw new InvalidOperationException("MessagesService:Token not configured")))
    .AddInterceptor(() => new ExceptionClientInterceptor());

// MassTransit RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PushNotificationConsumer>();
    x.AddConsumer<DismissPushConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? throw new InvalidOperationException("RabbitMQ:Host not configured"), "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? throw new InvalidOperationException("RabbitMQ:Username not configured"));
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? throw new InvalidOperationException("RabbitMQ:Password not configured"));
        });

        cfg.ReceiveEndpoint("push-notifications-handler", e =>
        {
            e.ConfigureConsumer<PushNotificationConsumer>(context);
        });

        cfg.ReceiveEndpoint("dismiss-push-handler", e =>
        {
            e.ConfigureConsumer<DismissPushConsumer>(context);
        });
    });
});

var app = builder.Build();

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();
