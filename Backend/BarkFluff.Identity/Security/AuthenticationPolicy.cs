using BarkFluff.Identity.Domain;
using BarkFluff.Proto.Identity;

namespace BarkFluff.Identity.Security;

public static class AuthenticationPolicy
{
    public static AuthLoginMode Mode(AuthUserProperty? settings) => settings?.LoginMode switch
    {
        AuthLoginMode.Password => AuthLoginMode.Password,
        AuthLoginMode.TelegramLogin => AuthLoginMode.TelegramLogin,
        AuthLoginMode.PasswordSecondFactor => AuthLoginMode.PasswordSecondFactor,
        _ => settings is { OtpEnabled: true } or { EmailOtpEnabled: true }
            ? AuthLoginMode.PasswordSecondFactor : AuthLoginMode.Password
    };

    public static bool TelegramAvailable(AuthUserProperty? settings) =>
        settings is { TelegramEnabled: true, TelegramId: not null };

    public static bool FactorEnabled(AuthUserProperty? settings, OtpTypeId factor) => factor switch
    {
        OtpTypeId.Authenticator => settings?.OtpEnabled == true,
        OtpTypeId.Email => settings?.EmailOtpEnabled == true,
        OtpTypeId.Telegram => TelegramAvailable(settings) && settings!.TelegramOtpEnabled,
        _ => false
    };

    public static OtpTypeId PreferredFactor(AuthUserProperty? settings)
    {
        var preferred = (OtpTypeId)(settings?.PreferredFactor ?? OtpType.Unknown);
        if (FactorEnabled(settings, preferred)) return preferred;
        return new[] { OtpTypeId.Authenticator, OtpTypeId.Email, OtpTypeId.Telegram }
            .FirstOrDefault(f => FactorEnabled(settings, f));
    }
}
