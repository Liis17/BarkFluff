using BarkFluff.Shared.Queue.Notifications;

namespace BarkFluff.Notification.Parsers;

public class HtmlEmailTemplateParser
{

    private readonly Dictionary<NotificationType, string> _templatesMap = new()
    {
        { NotificationType.ConfirmationRegistration, "confirmation_account.html"},
        { NotificationType.ConfirmationOtpEmail, "confirmation_otp_email.html"},
        { NotificationType.ConfirmationAuth, "confirmation_auth.html"},
        { NotificationType.ResetPassword, "reset_password.html"},
        { NotificationType.FailedLogin, "failed_login.html"},
        { NotificationType.SuccessfulRegistration, "successful_registration.html"},
        { NotificationType.SuccessfulLogin, "successful_login.html"},
        { NotificationType.PasswordChanged, "password_changed.html"},
        { NotificationType.TwoFactorMethodChanged, "two_factor_method_changed.html"},
    };
    
    public async Task<string> Parse(NotificationType type, Dictionary<string, string> payload)
    {
        var templateName = _templatesMap[type];

        var fileName = Path.Combine(Environment.CurrentDirectory, "Templates", templateName);
        
        var fileContent = await File.ReadAllTextAsync(fileName);

        foreach (var payloadItem in payload)
        {
            fileContent = fileContent.Replace($"ꟿꟿꟿ{payloadItem.Key}ꟿꟿꟿ", payloadItem.Value);
        }

        fileContent = fileContent.Replace("ꟿꟿꟿcurrentyearꟿꟿꟿ", DateTime.UtcNow.Year.ToString());

        return fileContent;
    }
}