using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BarkFluff.Client.WinUI.Infrastructure.Behaviors;

/// <summary>
/// Плеер создан скрытым и раскрывается по кнопке: держать <see cref="MediaPlayerElement"/>
/// активным в каждой плитке виртуализованной ленты слишком дорого.
/// </summary>
public static class PlayVideoBehavior
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(PlayVideoBehavior),
        new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    private static void OnEnableChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not Button button)
        {
            return;
        }

        button.Click -= OnButtonClick;
        if ((bool)eventArgs.NewValue)
        {
            button.Click += OnButtonClick;
        }
    }

    private static void OnButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var parent = VisualTreeHelper.GetParent(element);
        while (parent is not null and not Grid)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (parent is not Grid grid)
        {
            return;
        }

        foreach (var player in grid.Children.OfType<MediaPlayerElement>())
        {
            player.Visibility = Visibility.Visible;
            player.MediaPlayer?.Play();
        }

        if (sender is Button playButton)
        {
            playButton.Visibility = Visibility.Collapsed;
        }
    }
}
