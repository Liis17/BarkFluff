using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для DevicesSettingsPage.xaml
    /// </summary>
    public partial class DevicesSettingsPage : BaseSettingsPage
    {
        public override string Title => "Устройства";

        public DevicesSettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
