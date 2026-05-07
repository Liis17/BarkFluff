using BarkFluff.Notification.Configurations;
using BarkFluff.Notification.Helpers;
using BarkFluff.Notification.Parsers;
using BarkFluff.Shared.Queue.Notifications;

using System.Net;
using System.Net.Mail;

namespace BarkFluff.Notification.Senders;

public class EmailSender
{
    private readonly EmailConfiguration _emailConfiguration;
    private readonly HtmlEmailTemplateParser _templateParser;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(
        EmailConfiguration emailConfiguration,
        HtmlEmailTemplateParser templateParser,
        ILogger<EmailSender> logger)
    {
        _emailConfiguration = emailConfiguration;
        _templateParser = templateParser;
        _logger = logger;
    }

    public async Task SendEmail(EmailNotification notification)
    {
        _logger.LogInformation(
            "Начало отправки email на {Email} с темой '{Subject}'",
            EmailMasker.Mask(notification.Address),
            notification.Title
        );

        try
        {
            ServicePointManager.ServerCertificateValidationCallback =
                (sender, certificate, chain, errors) => true;

            using var smtpClient = new SmtpClient(_emailConfiguration.Host, _emailConfiguration.Port)
            {
                Credentials = new NetworkCredential(_emailConfiguration.SenderEmail, _emailConfiguration.SenderPassword),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            _logger.LogDebug(
                "Парсинг HTML шаблона для типа уведомления {NotificationType}",
                notification.Type
            );

            var body = await _templateParser.Parse(notification.Type, notification.Payload);

            using var mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(_emailConfiguration.SenderEmail);
            mailMessage.Subject = notification.Title;
            mailMessage.Body = body;
            mailMessage.IsBodyHtml = true;
            mailMessage.To.Add(new MailAddress(notification.Address));

            _logger.LogDebug(
                "Отправка email через SMTP сервер {Host}:{Port}",
                _emailConfiguration.Host,
                _emailConfiguration.Port
            );

            await smtpClient.SendMailAsync(mailMessage);

            _logger.LogInformation(
                "Email успешно отправлен на {Email}",
                EmailMasker.Mask(notification.Address)
            );
        }
        catch (SmtpException ex)
        {
            _logger.LogError(
                ex,
                "SMTP ошибка при отправке email на {Email}. StatusCode: {StatusCode}",
                EmailMasker.Mask(notification.Address),
                ex.StatusCode
            );
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Неожиданная ошибка при отправке email на {Email}",
                EmailMasker.Mask(notification.Address)
            );
            throw;
        }
    }
}