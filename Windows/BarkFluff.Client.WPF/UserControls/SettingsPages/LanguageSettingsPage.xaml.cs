using BarkFluff.Client.WPF.Services.App;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для LanguageSettingsPage.xaml.
    /// Переключение языка интерфейса: system / ru / en. Применяется мгновенно через
    /// <see cref="LanguageManager.Apply"/>, сохраняется в реестр и GlobalParam.
    /// </summary>
    public partial class LanguageSettingsPage : BaseSettingsPage
    {
        public override string TitleKey => "L_Settings_Sidebar_Language";

        private static readonly SolidColorBrush AccentBrush      = new(Color.FromRgb(0xDF, 0x50, 0x00));
        private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

        private readonly Dictionary<string, Ellipse> _radioMap;

        public LanguageSettingsPage()
        {
            InitializeComponent();

            _radioMap = new Dictionary<string, Ellipse>
            {
                { "system", RadioSystem },
                { "ru",     RadioRu     },
                { "en",     RadioEn     }
            };

            var currentLanguage = LanguageRegistryHelper.GetLanguage();
            UpdateRadioVisuals(currentLanguage);
        }

        private void LanguageOption_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string language) return;

            LanguageRegistryHelper.SetLanguage(language);

            if (App.GParam != null)
            {
                App.GParam.AppLanguage = language;
                App.SaveGlobalParam();
            }

            LanguageManager.Instance.Apply(language);
            UpdateRadioVisuals(language);
        }

        private void UpdateRadioVisuals(string selectedLanguage)
        {
            foreach (var (language, ellipse) in _radioMap)
            {
                if (language == selectedLanguage)
                {
                    ellipse.Stroke = AccentBrush;
                    ellipse.Fill   = AccentBrush;
                }
                else
                {
                    ellipse.Stroke = (SolidColorBrush)FindResource("DescriptionText");
                    ellipse.Fill   = TransparentBrush;
                }
            }
        }
    }
}
