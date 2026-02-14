using System.Windows;

using Wpf.Ui.Appearance;

namespace BarkFluff.Client.WPF.Debugs
{
    public partial class ThemeDebugWindow : Window
    {
        public ThemeDebugWindow()
        {
            InitializeComponent();
        }

        private void ApplyLight(object sender, RoutedEventArgs e)
        {
            App.ApplyTheme(ApplicationTheme.Light);
        }

        private void ApplyDark(object sender, RoutedEventArgs e)
        {
            App.ApplyTheme(ApplicationTheme.Dark);
        }
    }
}
