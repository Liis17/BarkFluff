namespace BarkFluff.Bots.Services.BotFather;

/// <summary>Состояние диалога с BotFather (хранится в BotFatherSessions.State)</summary>
public enum BotFatherState
{
    Idle = 0,

    /// <summary>/newbot: ожидание отображаемого имени</summary>
    AwaitingBotName = 1,

    /// <summary>/newbot: ожидание username (суффикс bot)</summary>
    AwaitingBotUsername = 2,

    /// <summary>/setname: ожидание нового имени</summary>
    AwaitingNewName = 3,

    /// <summary>/setdescription: ожидание описания</summary>
    AwaitingDescription = 4,

    /// <summary>/setuserpic: ожидание картинки</summary>
    AwaitingUserpic = 5,

    /// <summary>/deletebot: ожидание подтверждения</summary>
    AwaitingDeleteConfirmation = 6,
}
