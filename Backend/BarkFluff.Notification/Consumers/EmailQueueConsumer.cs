using BarkFluff.Notification.Senders;
using BarkFluff.Shared.Queue.Notifications;
using MassTransit;

namespace BarkFluff.Notification.Consumers;

public class EmailQueueConsumer : IConsumer<EmailNotification>
{
    
    private readonly EmailSender _emailSender;

    public EmailQueueConsumer(EmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public async Task Consume(ConsumeContext<EmailNotification> context)
    {
        await _emailSender.SendEmail(context.Message);
    }
}