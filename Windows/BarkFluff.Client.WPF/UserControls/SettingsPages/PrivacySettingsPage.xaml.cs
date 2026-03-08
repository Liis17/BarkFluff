namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для PrivacySettingsPage.xaml
    /// </summary>
    public partial class PrivacySettingsPage : BaseSettingsPage
    {
        public override string Title => "Конфиденциальность";

        public PrivacySettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
