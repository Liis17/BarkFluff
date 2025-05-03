using BarkFluff.Shared.Queue.Notifications;
using MassTransit;

namespace BarkFluff.Identity.Infrastructure;

public class NotificationQueueSender
{
    private readonly IBus rabbitMqBus;

    public NotificationQueueSender(IBus rabbitMqBus)
    {
        this.rabbitMqBus = rabbitMqBus;
    }


    public async Task SendNotification(Notification notification)
    {
        
        if (notification is EmailNotification emailNotification)
        {
            var endpoint = await rabbitMqBus.GetSendEndpoint(new Uri("queue:notifications-email"));
            
            await endpoint.Send(emailNotification);
            
            return;
        }
        
    }
}