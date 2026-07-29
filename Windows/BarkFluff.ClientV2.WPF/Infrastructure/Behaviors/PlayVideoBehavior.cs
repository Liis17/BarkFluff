using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Behaviors;

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
        while (parent is not null && parent is not Grid)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (parent is Grid grid)
        {
            foreach (var player in grid.Children.OfType<MediaElement>())
            {
                player.Visibility = Visibility.Visible;
                player.Play();
            }
        }
    }
}
