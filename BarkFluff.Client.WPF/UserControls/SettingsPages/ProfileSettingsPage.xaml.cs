using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для ProfileSettingsPage.xaml
    /// </summary>
    public partial class ProfileSettingsPage : BaseSettingsPage
    {
        public override string Title => "Профиль";

        public ProfileSettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
