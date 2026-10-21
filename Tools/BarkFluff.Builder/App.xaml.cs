using System.Windows;
using Wpf.Ui.Appearance;

namespace BarkFluff.Builder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.ApplySystemTheme();
    }
}
