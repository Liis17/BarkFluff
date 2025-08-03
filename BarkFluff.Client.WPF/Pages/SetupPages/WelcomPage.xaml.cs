using BarkFluff.Client.WPF.Services.Erida;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    public partial class WelcomePage : UserControl
    {
        public WelcomePage()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            App.MessengerWindow.OpenCreatePinCodePage();
            App.ErideMessage.AddMessage("Добро пожаловать в BarkFluff!", new MessageType { Type = MessageType.MessageTypeEnum.Info});
        }

        private void ClickFooter(object sender, MouseButtonEventArgs e)
        {
            MessageBoxOptions options = MessageBoxOptions.DefaultDesktopOnly | MessageBoxOptions.None;
            MessageBox.Show("BarkFluff Client WPF\nVersion: " + AppVersion.VersionName + " " + AppVersion.Version, "About", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.OK, options);
        }

        private void ClickPrivacyPolicyAndTermsOfUse(object sender, MouseButtonEventArgs e)
        {

        }
    }
}
