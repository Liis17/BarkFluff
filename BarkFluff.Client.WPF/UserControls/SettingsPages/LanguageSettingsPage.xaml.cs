using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для LanguageSettingsPage.xaml
    /// </summary>
    public partial class LanguageSettingsPage : BaseSettingsPage
    {
        public override string Title => "Язык";

        public LanguageSettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
