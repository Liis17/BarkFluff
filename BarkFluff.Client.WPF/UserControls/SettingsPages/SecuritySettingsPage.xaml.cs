using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для SecuritySettingsPage.xaml
    /// </summary>
    public partial class SecuritySettingsPage : BaseSettingsPage
    {
        public override string Title => "Защита";

        public SecuritySettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
