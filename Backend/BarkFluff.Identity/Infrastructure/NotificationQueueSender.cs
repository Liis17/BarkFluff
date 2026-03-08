using BarkFluff.Shared.Queue.Notifications;

using MassTransit;

namespace BarkFluff.Identity.Infrastructure;

public class NotificationQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public NotificationQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task SendNotification(Notification notification)
    {
        if (notification is EmailNotification emailNotification)
        {
            await _publishEndpoint.Publish(emailNotification);
        }
    }
}