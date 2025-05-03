using BarkFluff.GrpcServer;
using BarkFluff.Notification.Consumers;
using MassTransit;

namespace BarkFluff.Notification;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.SetRunningAddress(builder.Configuration);
        
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
        
        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<EmailQueueConsumer>();
    
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });
        
                cfg.ReceiveEndpoint("notifications-email", e =>
                {
                    e.ConfigureConsumer<EmailQueueConsumer>(context);
                });
            });
        });

        var app = builder.Build();
        app.UseRouting();

        app.Run();
    }
}