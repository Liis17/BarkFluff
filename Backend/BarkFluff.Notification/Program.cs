using BarkFluff.GrpcServer;
using BarkFluff.Notification.Configurations;
using BarkFluff.Notification.Consumers;
using BarkFluff.Notification.Parsers;
using BarkFluff.Notification.Senders;
using BarkFluff.Shared.Identity;
using MassTransit;
using Serilog;

namespace BarkFluff.Notification;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Notifications);
        builder.AddBarkFluffSerilog("BarkFluff.Notification");
        builder.SetRunningAddress(builder.Configuration);
        builder.Services.AddBarkFluffMetrics("BarkFluff.Notification");

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddSettings<EmailConfiguration>(builder.Configuration, "Email");
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
        
                cfg.ReceiveEndpoint("notifications-email-handler", e =>
                {
                    e.ConfigureConsumer<EmailQueueConsumer>(context);
                });
            });
        });

        builder.Services.AddTransient<EmailSender>();
        builder.Services.AddTransient<HtmlEmailTemplateParser>();

        var app = builder.Build();
        app.UseRouting();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}