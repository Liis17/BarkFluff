namespace BarkFluff.ClientV2.WPF.Models;

/// <summary>
/// Что делает приложение, когда пользователь закрывает главное окно.
/// </summary>
public enum WindowClosingBehavior
{
    /// <summary>Завершить работу приложения. Поведение по умолчанию.</summary>
    Exit,

    /// <summary>Скрыть окно и остаться в области уведомлений.</summary>
    MinimizeToTray
}
