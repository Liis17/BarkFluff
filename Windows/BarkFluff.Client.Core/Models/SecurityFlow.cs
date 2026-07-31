namespace BarkFluff.Client.Core.Models;

/// <summary>
/// Шаг раздела «Безопасность». Один перечислимый признак вместо нескольких флагов: сценарии
/// смены пароля и настройки 2FA взаимно исключают друг друга.
/// </summary>
public enum SecurityFlow
{
    None,
    PasswordRequest,
    PasswordCode,
    PasswordNew,
    TwoFactorSetup,
    TwoFactorDisable
}
