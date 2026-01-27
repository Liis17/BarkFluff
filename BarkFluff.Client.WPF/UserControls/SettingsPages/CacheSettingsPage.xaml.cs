using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для CacheSettingsPage.xaml
    /// </summary>
    public partial class CacheSettingsPage : BaseSettingsPage
    {
        public override string Title => "Кеш";

        public CacheSettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
