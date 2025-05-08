using System.Net;
using System.Net.Mail;
using BarkFluff.Notification.Configurations;
using BarkFluff.Notification.Parsers;
using BarkFluff.Shared.Queue.Notifications;

namespace BarkFluff.Notification.Senders;

public class EmailSender
{
    private readonly EmailConfiguration _emailConfiguration;
    private readonly HtmlEmailTemplateParser _templateParser;

    public EmailSender(EmailConfiguration emailConfiguration, HtmlEmailTemplateParser templateParser)
    {
        _emailConfiguration = emailConfiguration;
        _templateParser = templateParser;
    }

    public async Task SendEmail(EmailNotification notification)
    {
        ServicePointManager.ServerCertificateValidationCallback =
            (sender, certificate, chain, errors) => true;
        
        using var smtpClient = new SmtpClient(_emailConfiguration.Host, _emailConfiguration.Port)
        {
            Credentials = new NetworkCredential(_emailConfiguration.SenderEmail, _emailConfiguration.SenderPassword),
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        var body = await _templateParser.Parse(notification.Type, notification.Payload);
        
        using var mailMessage = new MailMessage();
        mailMessage.From = new MailAddress(_emailConfiguration.SenderEmail);
        mailMessage.Subject = notification.Title;
        mailMessage.Body = body;
        mailMessage.IsBodyHtml = true;
        mailMessage.To.Add(new MailAddress(notification.Address));

        await smtpClient.SendMailAsync(mailMessage);
    }
}