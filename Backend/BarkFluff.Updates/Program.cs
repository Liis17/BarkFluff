using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Consumers;
using BarkFluff.Updates.Host;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.LoadConfiguration(ServiceId.Updates);
builder.SetRunningAddress(builder.Configuration);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<ServerExceptionInterceptor>();
});

builder.Services.AddGrpcReflection();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

builder.Services.AddXAuth(builder.Configuration);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<NewMessageConsumer>();
    
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
    });
});

var app = builder.Build();
app.MapGrpcReflectionService();
app.UseRouting();
        
app.UseXAuth();
        
app.MapGrpcService<UpdatesApiService>();

app.Run();