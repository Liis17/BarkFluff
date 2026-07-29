using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.ClientV2.WPF.Views.Controls;

public partial class MessageBubbleControl : UserControl
{
    public MessageBubbleControl()
    {
        InitializeComponent();
    }

    private void OnPlayVideoClick(object sender, RoutedEventArgs e)
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
            foreach (var child in grid.Children.OfType<MediaElement>())
            {
                child.Visibility = Visibility.Visible;
                child.Play();
            }
        }
    }
}
