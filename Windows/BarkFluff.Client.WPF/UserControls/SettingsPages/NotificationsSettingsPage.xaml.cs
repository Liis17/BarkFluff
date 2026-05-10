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
        public override string Title => "Уведомления и звук";

        private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0xB3, 0x58, 0x44));
        private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

        private readonly Dictionary<NotificationDisplayMode, Ellipse> _radioMap;
        private bool _isLoadingFromGParam;

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

            var mode = App.GParam?.NotificationMode ?? NotificationDisplayMode.FullWithPreview;
            UpdateRadioVisuals(mode);

            _isLoadingFromGParam = true;
            try
            {
                SoundToggle.IsChecked = App.GParam?.NotificationSoundEnabled ?? true;
                SoundToggle.IsEnabled = mode != NotificationDisplayMode.Disabled;
            }
            finally
            {
                _isLoadingFromGParam = false;
            }
        }

        private async void Mode_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tagStr)
                return;

            if (!int.TryParse(tagStr, out int modeInt))
                return;

            var mode = (NotificationDisplayMode)modeInt;

            if (App.GParam != null)
            {
                App.GParam.NotificationMode = mode;
                App.SaveGlobalParam();
            }

            UpdateRadioVisuals(mode);
            SoundToggle.IsEnabled = mode != NotificationDisplayMode.Disabled;

            // Серверная синхронизация: уведомления включены если не Disabled
            try
            {
                await App.ServerCommunication.SetNotificationsEnabled(
                    mode != NotificationDisplayMode.Disabled, App.GParam);
            }
            catch
            {
                // ignored — сервер мог быть недоступен, локальное значение уже применено
            }
        }

        private void SoundToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingFromGParam || App.GParam == null) return;
            App.GParam.NotificationSoundEnabled = SoundToggle.IsChecked == true;
            App.SaveGlobalParam();
        }

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
                    ellipse.Stroke = (SolidColorBrush)FindResource("DescriptionText");
                    ellipse.Fill = TransparentBrush;
                }
            }
        }
    }
}
