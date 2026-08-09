using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.Media.Core;

namespace BarkFluff.Client.WinUI.Infrastructure.Behaviors;

/// <summary>
/// Плеер создан скрытым и раскрывается по кнопке: держать <see cref="MediaPlayerElement"/>
/// активным в каждой плитке виртуализованной ленты слишком дорого. По той же причине
/// <c>Source</c> назначается здесь, а не привязкой в шаблоне: <see cref="MediaSource"/>,
/// созданный на материализации плитки, открывал поток на каждое вложение — включая картинки —
/// и на переработке контейнеров лента падала с 0xC000027B внутри Microsoft.UI.Xaml.dll.
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
        if (sender is not Button playButton
            || !Uri.TryCreate(playButton.Tag as string, UriKind.Absolute, out var uri))
        {
            return;
        }

        var parent = VisualTreeHelper.GetParent(playButton);
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
            // Переработка контейнера возвращает кнопку Play, а прошлый источник остаётся открытым.
            (player.Source as MediaSource)?.Dispose();
            player.Source = MediaSource.CreateFromUri(uri);
            player.Visibility = Visibility.Visible;
            player.MediaPlayer?.Play();
        }

        playButton.Visibility = Visibility.Collapsed;
    }
}
