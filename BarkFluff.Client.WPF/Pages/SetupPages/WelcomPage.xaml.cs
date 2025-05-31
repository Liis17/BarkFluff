using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
