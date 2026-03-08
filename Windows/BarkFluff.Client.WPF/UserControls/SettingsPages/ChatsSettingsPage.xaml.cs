namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для ChatsSettingsPage.xaml
    /// </summary>
    public partial class ChatsSettingsPage : BaseSettingsPage
    {
        public override string Title => "Чаты";

        public ChatsSettingsPage()
        {
            InitializeComponent();
        }

        private void GoBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoBack();
        }
    }
}
