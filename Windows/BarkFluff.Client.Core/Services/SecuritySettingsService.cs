using BarkFluff.Client.Core.Models;

using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class SecuritySettingsService : SessionScopedService, ISecuritySettingsService
{
    public SecuritySettingsService(WebApiClient webApi, IClientSession session)
        : base(webApi, session)
    {
    }

    public async Task<(string? ErrorKey, bool AuthenticatorEnabled, bool EmailEnabled)> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await WebApi.OtpStatus(Parameters);
        return result.error.IsSuccess
            ? (null, result.authenticatorEnabled, result.emailEnabled)
            : ("Error_SettingsLoadFailed", false, false);
    }

    public async Task<(string? ErrorKey, string QrBase64, string ManualCode)> BeginTwoFactorSetupAsync(TwoFactorMethod method, CancellationToken cancellationToken = default)
    {
        var result = await WebApi.OtpReceipt(Parameters, ToOtpType(method));
        return result.error.IsSuccess
            ? (null, result.qrBase64 ?? string.Empty, result.justCode ?? string.Empty)
            : ("Error_TwoFactorFailed", string.Empty, string.Empty);
    }

    public async Task<string?> ConfirmTwoFactorAsync(string code, CancellationToken cancellationToken = default)
    {
        var result = await WebApi.OtpAccept(Parameters, code);
        return result.IsSuccess ? null : "Error_TwoFactorCodeInvalid";
    }

    public async Task<string?> DisableTwoFactorAsync(TwoFactorMethod method, string code, CancellationToken cancellationToken = default)
    {
        var result = await WebApi.OtpDisable(Parameters, ToOtpType(method), code);
        return result.IsSuccess ? null : "Error_TwoFactorFailed";
    }

    public async Task<(string? ErrorKey, string ResetId)> RequestPasswordCodeAsync(CancellationToken cancellationToken = default)
    {
        var parameters = Parameters;
        var result = await WebApi.ResetPassword(string.Empty, parameters.UserName, parameters);
        return result.error.IsSuccess && result.resetId is { Length: > 0 }
            ? (null, result.resetId)
            : ("Error_PasswordResetFailed", string.Empty);
    }

    /// <summary>
    /// Токен обновления, который возвращает подтверждение, сознательно отбрасывается.
    /// Применить его значило бы пересоздать gRPC-каналы посреди сессии и оборвать живые стримы
    /// сообщений и присутствия; Android в этом сценарии поступает так же.
    /// </summary>
    public async Task<string?> ConfirmPasswordCodeAsync(string resetId, string code, CancellationToken cancellationToken = default)
    {
        var result = await WebApi.ConfirmResetCode(resetId, code, Parameters);
        return result.error.IsSuccess ? null : "Error_PasswordResetCodeInvalid";
    }

    /// <summary>
    /// Старый пароль не передаётся: подтверждение кода очищает хеш на сервере, и по proto он
    /// обязателен только когда пароль всё ещё установлен.
    /// </summary>
    public async Task<string?> SetPasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        var result = await WebApi.SetPassword(newPassword, Parameters);
        return result.IsSuccess ? null : "Error_PasswordInvalid";
    }

    private static BarkFluff.Proto.Identity.OtpTypeId ToOtpType(TwoFactorMethod method) =>
        method == TwoFactorMethod.Email
            ? BarkFluff.Proto.Identity.OtpTypeId.Email
            : BarkFluff.Proto.Identity.OtpTypeId.Authenticator;
}
