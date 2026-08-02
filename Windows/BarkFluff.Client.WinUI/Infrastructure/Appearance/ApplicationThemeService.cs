using BarkFluff.Client.Core.Models;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace BarkFluff.Client.WinUI.Infrastructure.Appearance;

/// <summary>
/// Тем четыре, а <c>ThemeDictionaries</c> знает только Light/Dark/HighContrast, и
/// <c>ThemeResource</c> не перевычисляется при переходе Dark → BarkFluffDark (обе «тёмные»).
/// Поэтому семантические кисти объявлены синглтонами в <c>App.xaml</c>, а сервис
/// переписывает им <see cref="SolidColorBrush.Color"/> — это обновляет всех потребителей.
/// </summary>
public sealed class ApplicationThemeService : IApplicationThemeService
{
    private static readonly Color BarkFluffAccent = Color.FromArgb(0xFF, 0x81, 0x34, 0x1E);

    private static readonly string[] BrushKeys =
    [
        "OnboardingWindowBackgroundBrush",
        "OnboardingAccentSurfaceBrush",
        "OnboardingSubtleSurfaceBrush",
        "MessengerModalScrimBrush",
        "MessageMediaTimestampBrush",
        "MessageMediaTimestampTextBrush",
        "ChatUnreadBadgeTextBrush",
        "MessageOwnBubbleBrush",
        "MessageOwnBubbleTextBrush",
        "MessageOtherBubbleBrush",
        "MessageOtherBubbleTextBrush",
        "PresenceOnlineBrush"
    ];

    private FrameworkElement? _root;
    private ApplicationThemeMode _theme = ApplicationThemeMode.System;

    public void PrepareAccent(ApplicationThemeMode theme)
    {
        if (theme is not ApplicationThemeMode.BarkFluffDark)
        {
            return;
        }

        var resources = Application.Current.Resources;
        resources["SystemAccentColor"] = BarkFluffAccent;
        resources["SystemAccentColorLight1"] = Lighten(BarkFluffAccent, 0.2);
        resources["SystemAccentColorLight2"] = Lighten(BarkFluffAccent, 0.4);
        resources["SystemAccentColorLight3"] = Lighten(BarkFluffAccent, 0.6);
        resources["SystemAccentColorDark1"] = Darken(BarkFluffAccent, 0.2);
        resources["SystemAccentColorDark2"] = Darken(BarkFluffAccent, 0.4);
        resources["SystemAccentColorDark3"] = Darken(BarkFluffAccent, 0.6);
        resources["BarkFluffAccentColor"] = BarkFluffAccent;
    }

    public void Attach(FrameworkElement root)
    {
        if (_root is not null)
        {
            _root.ActualThemeChanged -= OnActualThemeChanged;
        }

        _root = root;
        _root.ActualThemeChanged += OnActualThemeChanged;
    }

    public void Apply(ApplicationThemeMode theme)
    {
        _theme = theme;

        if (_root is not null)
        {
            // RequestedTheme ставится на корень окна, а не на Application: сеттер приложения
            // валиден только до создания первого окна, а тема приезжает из SQLite позже.
            _root.RequestedTheme = ResolveElementTheme(theme);
        }

        ApplyPalette();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_theme is ApplicationThemeMode.System)
        {
            ApplyPalette();
        }
    }

    private void ApplyPalette()
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri($"ms-appx:///Resources/Colors/{ResolvePaletteName(_theme)}.xaml")
        };

        var resources = Application.Current.Resources;
        foreach (var brushKey in BrushKeys)
        {
            var colorKey = string.Concat(brushKey.AsSpan(0, brushKey.Length - "Brush".Length), "Color");
            if (resources.TryGetValue(brushKey, out var brushValue)
                && brushValue is SolidColorBrush brush
                && palette.TryGetValue(colorKey, out var colorValue)
                && colorValue is Color color)
            {
                brush.Color = color;
            }
        }
    }

    private ElementTheme ResolveElementTheme(ApplicationThemeMode theme) => theme switch
    {
        ApplicationThemeMode.Light => ElementTheme.Light,
        ApplicationThemeMode.Dark or ApplicationThemeMode.BarkFluffDark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private string ResolvePaletteName(ApplicationThemeMode theme) => theme switch
    {
        ApplicationThemeMode.Light => "Light",
        ApplicationThemeMode.Dark => "Dark",
        ApplicationThemeMode.BarkFluffDark => "BarkFluffDark",
        _ => _root?.ActualTheme is ElementTheme.Dark ? "Dark" : "Light"
    };

    private static Color Lighten(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)(color.R + ((255 - color.R) * amount)),
        (byte)(color.G + ((255 - color.G) * amount)),
        (byte)(color.B + ((255 - color.B) * amount)));

    private static Color Darken(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)(color.R * (1 - amount)),
        (byte)(color.G * (1 - amount)),
        (byte)(color.B * (1 - amount)));
}
