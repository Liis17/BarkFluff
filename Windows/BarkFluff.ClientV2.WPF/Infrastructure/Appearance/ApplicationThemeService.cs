using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;

using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Appearance;

public sealed class ApplicationThemeService : IApplicationThemeService
{
    private const string ColorsDirectory = "Resources/Colors/";

    public void Apply(ApplicationThemeMode theme)
    {
        var applicationTheme = ResolveTheme(theme);
        ApplicationThemeManager.Apply(
            applicationTheme,
            WindowBackdropType.Mica,
            updateAccent: theme is not ApplicationThemeMode.BarkFluffDark);

        if (theme is ApplicationThemeMode.BarkFluffDark)
        {
            ApplicationAccentColorManager.Apply(
                Color.FromRgb(0x81, 0x34, 0x1E),
                applicationTheme,
                systemGlassColor: false,
                systemAccentColor: true);
        }

        ReplaceColorDictionary(theme);
    }

    public void WatchSystemTheme(MainWindow window, ApplicationThemeMode theme)
    {
        if (theme is ApplicationThemeMode.System)
        {
            SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, updateAccents: true);
        }
    }

    private static ApplicationTheme ResolveTheme(ApplicationThemeMode theme) => theme switch
    {
        ApplicationThemeMode.Light => ApplicationTheme.Light,
        ApplicationThemeMode.Dark or ApplicationThemeMode.BarkFluffDark => ApplicationTheme.Dark,
        _ => ApplicationThemeManager.GetSystemTheme().ToString().Contains("Dark", StringComparison.Ordinal)
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light
    };

    private static void ReplaceColorDictionary(ApplicationThemeMode theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains(ColorsDirectory, StringComparison.OrdinalIgnoreCase) is true);

        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        var dictionaryName = theme switch
        {
            ApplicationThemeMode.Light => "Light.xaml",
            ApplicationThemeMode.Dark => "Dark.xaml",
            ApplicationThemeMode.BarkFluffDark => "BarkFluffDark.xaml",
            _ => ResolveTheme(theme) is ApplicationTheme.Dark ? "Dark.xaml" : "Light.xaml"
        };

        dictionaries.Insert(2, new ResourceDictionary
        {
            Source = new Uri($"{ColorsDirectory}{dictionaryName}", UriKind.Relative)
        });
    }
}
