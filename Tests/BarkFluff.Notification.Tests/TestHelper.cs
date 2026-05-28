using BarkFluff.Notification.Configurations;
using BarkFluff.Notification.Consumers;
using BarkFluff.Notification.Parsers;
using BarkFluff.Notification.Senders;
using BarkFluff.Shared.Queue.Notifications;

using MassTransit;

namespace BarkFluff.Notification.Tests;

public class TestHelper
{
    public MetricsCollector Metrics { get; }
    public EmailConfiguration EmailConfig { get; }

    public TestHelper()
    {
        Metrics = new MetricsCollector();
        EmailConfig = new EmailConfiguration
        {
            Host = "smtp.test.com",
            Port = 587,
            SenderEmail = "noreply@barkfluff.test",
            SenderPassword = "test-password"
        };
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return Mock.Of<ILogger<T>>();
    }

    public static EmailNotification CreateEmailNotification(
        string address = "user@example.com",
        string title = "Test Subject",
        NotificationType type = NotificationType.ConfirmationRegistration,
        Dictionary<string, string>? payload = null)
    {
        return new EmailNotification
        {
            Address = address,
            Title = title,
            Type = type,
            Payload = payload ?? new Dictionary<string, string>(),
            CreatedAt = DateTime.UtcNow,
            OwnerId = 1,
            ServiceId = Shared.Identity.ServiceId.Notifications
        };
    }

    public static Mock<ConsumeContext<EmailNotification>> CreateConsumeContext(EmailNotification notification)
    {
        var context = new Mock<ConsumeContext<EmailNotification>>();
        context.Setup(c => c.Message).Returns(notification);
        return context;
    }

    public EmailQueueConsumer CreateConsumer(EmailSender emailSender)
    {
        return new EmailQueueConsumer(emailSender, CreateLogger<EmailQueueConsumer>(), Metrics);
    }

    public EmailSender CreateEmailSender(HtmlEmailTemplateParser templateParser)
    {
        return new EmailSender(EmailConfig, templateParser, CreateLogger<EmailSender>());
    }
}
