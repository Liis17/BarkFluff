namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для CloudSettingsPage.xaml
    /// </summary>
    public partial class CloudSettingsPage : BaseSettingsPage
    {
        public override string Title => "Облако";

        public CloudSettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
