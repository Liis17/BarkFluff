namespace BarkFluff.Client.Core.Models;

/// <summary>
/// Способ двухфакторной аутентификации. Отдельный от <c>OtpTypeId</c> тип: protobuf остаётся
/// внутри сервисного слоя, а ViewModel и тесты работают с обычным перечислением.
/// </summary>
public enum TwoFactorMethod
{
    Authenticator,
    Email
}
