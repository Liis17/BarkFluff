using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    /// <summary>
    /// Логика взаимодействия для WelcomPage.xaml
    /// </summary>
    public partial class WelcomPage : UserControl
    {
        public WelcomPage()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            App.MessengerWindow.OpenCreatePinCodePage();
        }

        private void ClickFooter(object sender, MouseButtonEventArgs e)
        {
            MessageBoxOptions options = MessageBoxOptions.DefaultDesktopOnly | MessageBoxOptions.None;
            MessageBox.Show("BarkFluff Client WPF\nVersion: " + AppVersion.VersionName + " " + AppVersion.Version, "About", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.OK, options);
        }
    }
}
