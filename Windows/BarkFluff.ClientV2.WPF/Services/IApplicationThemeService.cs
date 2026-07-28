using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public interface IApplicationThemeService
{
    void Apply(ApplicationThemeMode theme);

    void WatchSystemTheme(MainWindow window, ApplicationThemeMode theme);
}
