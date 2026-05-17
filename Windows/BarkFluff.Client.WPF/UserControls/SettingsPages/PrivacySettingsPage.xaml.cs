using BarkFluff.Proto.Users;

using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Страница настроек конфиденциальности.
    /// Загружает и сохраняет <see cref="PrivacySettings"/> через <see cref="App.ServerCommunication"/>.
    /// </summary>
    public partial class PrivacySettingsPage : BaseSettingsPage
    {
        public override string TitleKey => "L_Settings_Privacy_Title";

        /// <summary>Флаг подавления событий SelectionChanged/Checked во время программной загрузки.</summary>
        private bool _loading = false;

        public PrivacySettingsPage()
        {
            InitializeComponent();
        }

        /// <summary>Вызывается при переходе на страницу — загружает актуальные настройки с сервера.</summary>
        public override void OnNavigatedTo()
        {
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            var webApi = App.ServerCommunication;
            var gParam = App.GParam;
            if (webApi == null || gParam == null) return;

            SetStatus(L("L_Settings_Privacy_LoadingSettings"));

            var (error, settings) = await webApi.GetPrivacySettings(gParam);
            if (!error.IsSuccess || settings == null)
            {
                var fmt = L("L_Settings_Privacy_LoadError");
                SetStatus(string.Format(fmt, error.ErrorMessage));
                return;
            }

            _loading = true;
            try
            {
                ProfileVisibleSwitch.IsChecked = settings.ProfileVisibleOnSite;
                SearchVisibleSwitch.IsChecked = settings.SearchVisible;

                SelectComboByTag(OnlineVisibilityCombo, (int)settings.OnlineVisibility);
                SelectComboByTag(AvatarVisibilityCombo, (int)settings.AvatarVisibility);
                SelectComboByTag(BioVisibilityCombo, (int)settings.BioVisibility);
                SelectComboByTag(EmailVisibilityCombo, (int)settings.EmailVisibility);
            }
            finally
            {
                _loading = false;
            }

            SetStatus(string.Empty);
        }

        private async Task SaveAsync()
        {
            if (_loading) return;

            var webApi = App.ServerCommunication;
            var gParam = App.GParam;
            if (webApi == null || gParam == null) return;

            var settings = new PrivacySettings
            {
                ProfileVisibleOnSite = ProfileVisibleSwitch.IsChecked == true,
                SearchVisible = SearchVisibleSwitch.IsChecked == true,
                OnlineVisibility = GetComboVisibility(OnlineVisibilityCombo),
                AvatarVisibility = GetComboVisibility(AvatarVisibilityCombo),
                BioVisibility = GetComboVisibility(BioVisibilityCombo),
                EmailVisibility = GetComboVisibility(EmailVisibilityCombo),
            };

            var error = await webApi.UpdatePrivacySettings(settings, gParam);
            if (error.IsSuccess)
            {
                SetStatus(string.Empty);
            }
            else
            {
                var fmt = L("L_Settings_Privacy_SaveError");
                SetStatus(string.Format(fmt, error.ErrorMessage));
            }
        }

        // --- Обработчики событий UI ---

        private void ProfileVisibleSwitch_Changed(object sender, RoutedEventArgs e) => _ = SaveAsync();
        private void SearchVisibleSwitch_Changed(object sender, RoutedEventArgs e) => _ = SaveAsync();
        private void OnlineVisibilityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = SaveAsync();
        private void AvatarVisibilityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = SaveAsync();
        private void BioVisibilityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = SaveAsync();
        private void EmailVisibilityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = SaveAsync();

        // --- Вспомогательные методы ---

        private static void SelectComboByTag(ComboBox combo, int tagValue)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag is string s && int.TryParse(s, out int v) && v == tagValue)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private static ProfileFieldVisibility GetComboVisibility(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item &&
                item.Tag is string s &&
                int.TryParse(s, out int v))
            {
                return (ProfileFieldVisibility)v;
            }
            return ProfileFieldVisibility.All;
        }

        private void SetStatus(string message)
        {
            Dispatcher.Invoke(() => StatusText.Text = message);
        }

        private static string L(string key)
            => Application.Current?.TryFindResource(key) as string ?? key;
    }
}
