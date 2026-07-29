using System.Windows.Controls;
using System.Windows.Data;

namespace BarkFluff.ClientV2.WPF.Views.Controls;

public partial class ChatListItemControl : UserControl
{
    public ChatListItemControl()
    {
        InitializeComponent();
    }

    private void OnAvatarSourceUpdated(object sender, DataTransferEventArgs e)
    {
        Initials.Visibility = AvatarImage.Source is null
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void OnAvatarImageFailed(object sender, System.Windows.ExceptionRoutedEventArgs e)
    {
        Initials.Visibility = System.Windows.Visibility.Visible;
    }
}
