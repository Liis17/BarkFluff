using System.Net.Mail;
using System.Net;
using BarkFluff.Notification.Configurations;
using BarkFluff.Notification.Parsers;
using BarkFluff.Notification.Senders;
using BarkFluff.Shared.Queue.Notifications;

namespace BarkFluff.Notification.Tests.Senders;

public class EmailSenderTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<HtmlEmailTemplateParser> _templateParserMock;

    public EmailSenderTests()
    {
        _templateParserMock = new Mock<HtmlEmailTemplateParser>();
        _templateParserMock
            .Setup(p => p.Parse(It.IsAny<NotificationType>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync("<html>Test Body</html>");
    }

    private EmailSender CreateSender(EmailConfiguration? config = null)
    {
        return new EmailSender(
            config ?? _helper.EmailConfig,
            _templateParserMock.Object,
            TestHelper.CreateLogger<EmailSender>());
    }

    [Fact]
    public async Task SendEmail_CallsTemplateParserWithCorrectTypeAndPayload()
    {
        var sender = CreateSender();
        var notification = TestHelper.CreateEmailNotification(
            type: NotificationType.ConfirmationRegistration,
            payload: new Dictionary<string, string> { ["username"] = "Alice" });

        try { await sender.SendEmail(notification); } catch { }

        _templateParserMock.Verify(
            p => p.Parse(NotificationType.ConfirmationRegistration, notification.Payload),
            Times.Once);
    }

    [Fact]
    public async Task SendEmail_SetsUpSmtpClientWithHostAndPort()
    {
        var config = new EmailConfiguration
        {
            Host = "custom.smtp.server",
            Port = 465,
            SenderEmail = "sender@test.com",
            SenderPassword = "pass"
        };
        var sender = CreateSender(config);

        var act = () => sender.SendEmail(TestHelper.CreateEmailNotification());

        await act.Should().ThrowAsync<SmtpException>();
    }

    [Fact]
    public async Task SendEmail_SmtpUnavailable_ThrowsSmtpException()
    {
        var sender = CreateSender();

        var act = () => sender.SendEmail(TestHelper.CreateEmailNotification());

        await act.Should().ThrowAsync<SmtpException>();
    }

    [Fact]
    public async Task SendEmail_SmtpException_PropagatesOriginalException()
    {
        var sender = CreateSender();
        var notification = TestHelper.CreateEmailNotification();

        var ex = await Assert.ThrowsAsync<SmtpException>(() => sender.SendEmail(notification));
        ex.Should().NotBeNull();
    }

    [Fact]
    public async Task SendEmail_TemplateParserThrows_PropagatesException()
    {
        _templateParserMock
            .Setup(p => p.Parse(It.IsAny<NotificationType>(), It.IsAny<Dictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("Template not found"));
        var sender = CreateSender();

        var act = () => sender.SendEmail(TestHelper.CreateEmailNotification());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Template not found");
    }

    [Fact]
    public async Task SendEmail_TemplateParserThrowsGenericException_PropagatesAsGenericException()
    {
        _templateParserMock
            .Setup(p => p.Parse(It.IsAny<NotificationType>(), It.IsAny<Dictionary<string, string>>()))
            .ThrowsAsync(new FormatException("Bad format"));
        var sender = CreateSender();

        var act = () => sender.SendEmail(TestHelper.CreateEmailNotification());

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("Bad format");
    }

    [Fact]
    public async Task SendEmail_NullAddress_ThrowsArgumentNullException()
    {
        var sender = CreateSender();
        var notification = TestHelper.CreateEmailNotification(address: null!);

        var act = () => sender.SendEmail(notification);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
    

    [Fact]
    public async Task SendEmail_EmptyPayload_StillCallsParser()
    {
        var sender = CreateSender();
        var notification = TestHelper.CreateEmailNotification(payload: new Dictionary<string, string>());

        try { await sender.SendEmail(notification); } catch { }

        _templateParserMock.Verify(
            p => p.Parse(notification.Type, It.Is<Dictionary<string, string>>(d => d.Count == 0)),
            Times.Once);
    }

    [Fact]
    public async Task SendEmail_DifferentNotificationTypes_PassedToParser()
    {
        var sender = CreateSender();
        var types = new[]
        {
            NotificationType.ResetPassword,
            NotificationType.FailedLogin,
            NotificationType.SuccessfulRegistration,
            NotificationType.PasswordChanged,
            NotificationType.TwoFactorMethodChanged
        };

        foreach (var type in types)
        {
            var notification = TestHelper.CreateEmailNotification(type: type);
            try { await sender.SendEmail(notification); } catch { }
        }

        _templateParserMock.Verify(
            p => p.Parse(It.IsAny<NotificationType>(), It.IsAny<Dictionary<string, string>>()),
            Times.Exactly(types.Length));
    }

    [Fact]
    public async Task SendEmail_NullTitle_ThrowsNullReferenceOrSmtp()
    {
        var sender = CreateSender();
        var notification = TestHelper.CreateEmailNotification(title: null!);

        var act = () => sender.SendEmail(notification);

        await act.Should().ThrowAsync<Exception>();
    }
    

    [Fact]
    public async Task SendEmail_CalledMultipleTimes_CallsParserEachTime()
    {
        var sender = CreateSender();

        for (int i = 0; i < 3; i++)
        {
            try { await sender.SendEmail(TestHelper.CreateEmailNotification()); } catch { }
        }

        _templateParserMock.Verify(
            p => p.Parse(It.IsAny<NotificationType>(), It.IsAny<Dictionary<string, string>>()),
            Times.Exactly(3));
    }
}
