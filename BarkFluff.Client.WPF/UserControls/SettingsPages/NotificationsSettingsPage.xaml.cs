using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для NotificationsSettingsPage.xaml
    /// </summary>
    public partial class NotificationsSettingsPage : BaseSettingsPage
    {
        public override string Title => "Уведомления";

        public NotificationsSettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
