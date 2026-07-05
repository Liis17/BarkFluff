namespace BarkFluff.Bots.Domain;

/// <summary>
/// Роль системного бота. Системные боты работают внутри сервиса Bots (in-process),
/// не через внешний токен/HTTP и не под rate-limit.
/// </summary>
public enum SystemBotRole
{
    None = 0,

    /// <summary>Создание и управление ботами через диалог (@botfather)</summary>
    BotFather = 1,

    /// <summary>Уведомления о входе в аккаунт</summary>
    LoginNotifier = 2,
}
