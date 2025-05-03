using BarkFluff.Shared.Queue.Notifications;
using MassTransit;

namespace BarkFluff.Notification.Consumers;

public class EmailQueueConsumer : IConsumer<EmailNotification>
{
    public Task Consume(ConsumeContext<EmailNotification> context)
    {
        Console.WriteLine("Отправка сообщения на почту....");
        
        return Task.CompletedTask;
    }
}