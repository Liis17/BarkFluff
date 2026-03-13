using BarkFluff.Client.WPF.Services.App;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using Wpf.Ui.Appearance;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для ChatsSettingsPage.xaml
    /// </summary>
    public partial class ChatsSettingsPage : BaseSettingsPage
    {
        public override string Title => "Чаты";

        private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0xDF, 0x50, 0x00));
        private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

        private readonly Dictionary<string, Ellipse> _radioMap;

        public ChatsSettingsPage()
        {
            InitializeComponent();

            _radioMap = new Dictionary<string, Ellipse>
            {
                { "light", RadioLight },
                { "dark", RadioDark },
                { "system", RadioSystem }
            };

            var currentTheme = ThemeRegistryHelper.GetTheme();
            UpdateRadioVisuals(currentTheme);
        }

        private void ThemeOption_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string theme)
                return;

            ThemeRegistryHelper.SetTheme(theme);

            ApplicationTheme appTheme = theme switch
            {
                "dark" => ApplicationTheme.Dark,
                "system" => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                    ? ApplicationTheme.Dark
                    : ApplicationTheme.Light,
                _ => ApplicationTheme.Light
            };

            App.ApplyTheme(appTheme);
            UpdateRadioVisuals(theme);
        }

        private void UpdateRadioVisuals(string selectedTheme)
        {
            foreach (var (theme, ellipse) in _radioMap)
            {
                if (theme == selectedTheme)
                {
                    ellipse.Stroke = AccentBrush;
                    ellipse.Fill = AccentBrush;
                }
                else
                {
                    ellipse.Stroke = (SolidColorBrush)FindResource("DescriptionText");
                    ellipse.Fill = TransparentBrush;
                }
            }
        }
    }
}
