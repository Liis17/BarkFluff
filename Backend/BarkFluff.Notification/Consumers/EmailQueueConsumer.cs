using BarkFluff.Notification.Senders;
using BarkFluff.Shared.Queue.Notifications;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Notification.Consumers;

public class EmailQueueConsumer : IConsumer<EmailNotification>
{
    private readonly EmailSender _emailSender;
    private readonly ILogger<EmailQueueConsumer> _logger;

    public EmailQueueConsumer(EmailSender emailSender, ILogger<EmailQueueConsumer> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EmailNotification> context)
    {
        var notification = context.Message;

        _logger.LogInformation(
            "Получено уведомление для отправки email. Адрес: {Email}, Тип: {Type}, Заголовок: '{Title}'",
            notification.Address,
            notification.Type,
            notification.Title
        );

        try
        {
            await _emailSender.SendEmail(notification);

            _logger.LogInformation(
                "Email успешно отправлен на адрес {Email}",
                notification.Address
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ошибка при отправке email на адрес {Email}",
                notification.Address
            );
            throw; // MassTransit обработает retry
        }
    }
}