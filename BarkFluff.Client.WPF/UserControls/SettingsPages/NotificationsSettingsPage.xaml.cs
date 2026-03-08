using BarkFluff.WebApi.Core.MessengerData;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для NotificationsSettingsPage.xaml
    /// </summary>
    public partial class NotificationsSettingsPage : BaseSettingsPage
    {
        public override string Title => "Уведомления";

        private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0xB3, 0x58, 0x44)); // #FFB35844
        private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(0xA0, 0xA0, 0xA0));
        private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

        /// <summary>
        /// Маппинг enum-значений на Ellipse-индикаторы
        /// </summary>
        private readonly Dictionary<NotificationDisplayMode, Ellipse> _radioMap;

        public NotificationsSettingsPage()
        {
            InitializeComponent();

            _radioMap = new Dictionary<NotificationDisplayMode, Ellipse>
            {
                { NotificationDisplayMode.Disabled, RadioDisabled },
                { NotificationDisplayMode.HiddenContent, RadioHidden },
                { NotificationDisplayMode.SenderOnly, RadioSenderOnly },
                { NotificationDisplayMode.FullTextNoPreview, RadioFullText },
                { NotificationDisplayMode.FullWithPreview, RadioFullPreview }
            };

            // Устанавливаем текущий выбранный режим
            UpdateRadioVisuals(App.GParam?.NotificationMode ?? NotificationDisplayMode.FullWithPreview);
        }

        private void GoBack_Click(object sender, RoutedEventArgs e)
        {
            GoBack();
        }

        private void Mode_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tagStr)
                return;

            if (!int.TryParse(tagStr, out int modeInt))
                return;

            var mode = (NotificationDisplayMode)modeInt;

            // Сохраняем настройку
            if (App.GParam != null)
            {
                App.GParam.NotificationMode = mode;
            }

            UpdateRadioVisuals(mode);
        }

        /// <summary>
        /// Обновляет визуальные индикаторы (radio-кнопки)
        /// </summary>
        private void UpdateRadioVisuals(NotificationDisplayMode selectedMode)
        {
            foreach (var (mode, ellipse) in _radioMap)
            {
                if (mode == selectedMode)
                {
                    ellipse.Stroke = AccentBrush;
                    ellipse.Fill = AccentBrush;
                }
                else
                {
                    ellipse.Stroke = InactiveBrush;
                    ellipse.Fill = TransparentBrush;
                }
            }
        }
    }
}
