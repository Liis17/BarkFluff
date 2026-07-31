using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

/// <summary>
/// Двухфакторная аутентификация и смена пароля из раздела настроек.
/// </summary>
/// <remarks>
/// Ошибка возвращается ключом словаря локализации, а не текстом: русские строки, зашитые в
/// менеджеры <c>WebApi.Core</c>, наружу не проходят. <c>null</c> означает успех.
/// </remarks>
public interface ISecuritySettingsService
{
    Task<(string? ErrorKey, bool AuthenticatorEnabled, bool EmailEnabled)> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Начинает подключение метода. Для <see cref="TwoFactorMethod.Email"/> сервер отправляет код
    /// письмом, поэтому QR и код для ручного ввода приходят пустыми.
    /// </summary>
    Task<(string? ErrorKey, string QrBase64, string ManualCode)> BeginTwoFactorSetupAsync(TwoFactorMethod method, CancellationToken cancellationToken = default);

    Task<string?> ConfirmTwoFactorAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Отключает метод. Код нужен только для приложения-аутентификатора.</summary>
    Task<string?> DisableTwoFactorAsync(TwoFactorMethod method, string code, CancellationToken cancellationToken = default);

    Task<(string? ErrorKey, string ResetId)> RequestPasswordCodeAsync(CancellationToken cancellationToken = default);

    Task<string?> ConfirmPasswordCodeAsync(string resetId, string code, CancellationToken cancellationToken = default);

    Task<string?> SetPasswordAsync(string newPassword, CancellationToken cancellationToken = default);
}
