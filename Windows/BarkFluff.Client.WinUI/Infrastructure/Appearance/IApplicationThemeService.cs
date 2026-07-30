using BarkFluff.Client.Core.Models;

using Microsoft.UI.Xaml;

namespace BarkFluff.Client.WinUI.Infrastructure.Appearance;

public interface IApplicationThemeService
{
    /// <summary>
    /// Пишет accent-ресурсы в <c>Application.Resources</c>. Обязано вызываться до создания
    /// контента окна: встроенные <c>AccentFillColor*Brush</c> резолвятся из них при парсинге.
    /// </summary>
    void PrepareAccent(ApplicationThemeMode theme);

    /// <summary>
    /// Привязывает сервис к корню окна: <c>RequestedTheme</c> ставится на элемент, а не на
    /// <c>Application</c>, потому что тема приезжает из SQLite уже после создания окна.
    /// </summary>
    void Attach(FrameworkElement root);

    void Apply(ApplicationThemeMode theme);
}
