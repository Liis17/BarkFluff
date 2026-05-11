using BarkFluff.Shared.Queue.Notifications;

using System.Net;
using System.Text.RegularExpressions;

namespace BarkFluff.Notification.Parsers;

public partial class HtmlEmailTemplateParser
{
    private static readonly Regex PlaceholderRegex = MyRegex();

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
        { NotificationType.PasswordChangedByAdmin, "password_changed_by_admin.html"},
    };

    public async Task<string> Parse(NotificationType type, Dictionary<string, string> payload)
    {
        var templateName = _templatesMap[type];

        var fileName = Path.Combine(Environment.CurrentDirectory, "Templates", templateName);

        var fileContent = await File.ReadAllTextAsync(fileName);

        var allPayload = new Dictionary<string, string>(payload)
        {
            ["currentyear"] = DateTime.UtcNow.Year.ToString(),
        };

        return PlaceholderRegex.Replace(fileContent, match =>
        {
            var key = match.Groups[1].Value;
            return allPayload.TryGetValue(key, out var value)
                ? WebUtility.HtmlEncode(value)
                : match.Value;
        });
    }

    [GeneratedRegex(@"ꟿꟿꟿ(\w+)ꟿꟿꟿ", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
