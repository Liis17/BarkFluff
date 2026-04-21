using BarkFluff.Client.WPF.Services.App;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using Microsoft.Win32;

using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Wpf.Ui.Appearance;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    public partial class ChatsSettingsPage : BaseSettingsPage
    {
        public override string Title => "Чаты";

        private static readonly SolidColorBrush AccentBrush      = new(Color.FromRgb(0xDF, 0x50, 0x00));
        private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

        private readonly Dictionary<string, Ellipse> _radioMap;
        private readonly List<string> _backgroundFileIds = [];
        private bool _bgLoaded = false;

        public ChatsSettingsPage()
        {
            InitializeComponent();

            _radioMap = new Dictionary<string, Ellipse>
            {
                { "light",  RadioLight  },
                { "dark",   RadioDark   },
                { "system", RadioSystem }
            };

            var currentTheme = ThemeRegistryHelper.GetTheme();
            UpdateRadioVisuals(currentTheme);

            var radius = App.GParam?.MessageBubbleCornerRadius ?? 12;
            CornerSlider.Value    = radius;
            CornerValueLabel.Text = radius.ToString();
        }

        public override void OnNavigatedTo()
        {
            if (!_bgLoaded)
            {
                _bgLoaded = true;
                _ = LoadBackgroundsAsync();
            }
        }

        private void ThemeOption_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string theme) return;

            ThemeRegistryHelper.SetTheme(theme);

            ApplicationTheme appTheme = theme switch
            {
                "dark"   => ApplicationTheme.Dark,
                "system" => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                              ? ApplicationTheme.Dark : ApplicationTheme.Light,
                _        => ApplicationTheme.Light
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
                    ellipse.Fill   = AccentBrush;
                }
                else
                {
                    ellipse.Stroke = (SolidColorBrush)FindResource("DescriptionText");
                    ellipse.Fill   = TransparentBrush;
                }
            }
        }

        private void CornerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int val = (int)e.NewValue;
            if (CornerValueLabel != null)
                CornerValueLabel.Text = val.ToString();

            if (App.GParam != null)
                App.GParam.MessageBubbleCornerRadius = val;
        }

        private async Task LoadBackgroundsAsync()
        {
            SetBgStatus("Загрузка...");
            BackgroundsGrid.ItemsSource = null;
            _backgroundFileIds.Clear();

            var webApi = App.ServerCommunication;
            var gParam = App.GParam;
            if (webApi == null || gParam == null) { SetBgStatus("Нет подключения"); return; }

            var (error, data) = await webApi.GetPersonalization(gParam);
            if (!error.IsSuccess || data == null) { SetBgStatus($"Ошибка: {error.ErrorMessage}"); return; }

            _backgroundFileIds.AddRange(data.ChatBackgroundFileIds);
            SetBgStatus(string.Empty);

            await RebuildGridAsync();
        }

        private async Task RebuildGridAsync()
        {
            var items = new List<BackgroundItem>();

            foreach (var fileId in _backgroundFileIds)
            {
                var (err, url) = await App.ServerCommunication.GetFile(App.GParam, fileId);
                if (!err.IsSuccess || string.IsNullOrEmpty(url)) continue;

                var source = await App.FileCacheService.GetCachedImageAsync(fileId, FileType.Image, url);
                items.Add(new BackgroundItem(fileId, source));
            }

            BackgroundsGrid.ItemsSource = items;
        }

        private async void AddBackground_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = false,
                Filter = "Изображения (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Все файлы (*.*)|*.*",
                Title  = "Выберите фоновое изображение"
            };

            if (dialog.ShowDialog() != true) return;

            AddBackgroundBtn.IsEnabled = false;
            SetBgStatus("Загрузка...");

            try
            {
                var (err, fileId) = await App.ServerCommunication.UploadFileAsync(
                    App.GParam,
                    dialog.FileName,
                    UploadFileType.MessageAttachmentImage);

                if (!err.IsSuccess || string.IsNullOrEmpty(fileId))
                {
                    SetBgStatus($"Ошибка загрузки: {err.ErrorMessage}");
                    return;
                }

                _backgroundFileIds.Add(fileId);
                await SavePersonalizationAsync();
                await RebuildGridAsync();
                SetBgStatus(string.Empty);
            }
            catch (Exception ex)
            {
                SetBgStatus($"Ошибка: {ex.Message}");
            }
            finally
            {
                AddBackgroundBtn.IsEnabled = true;
            }
        }

        private async void DeleteBackground_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem mi) return;
            var fileId = mi.Tag as string;
            if (string.IsNullOrEmpty(fileId)) return;

            _backgroundFileIds.Remove(fileId);
            await SavePersonalizationAsync();
            await RebuildGridAsync();
        }

        private async Task SavePersonalizationAsync()
        {
            var data = new UserPersonalizationData();
            data.ChatBackgroundFileIds.AddRange(_backgroundFileIds);

            var error = await App.ServerCommunication.UpdatePersonalization(data, App.GParam);
            if (!error.IsSuccess)
                SetBgStatus($"Ошибка сохранения: {error.ErrorMessage}");
        }

        private void SetBgStatus(string msg)
            => Dispatcher.Invoke(() => BgStatusText.Text = msg);
    }

    internal sealed class BackgroundItem(string fileId, System.Windows.Media.ImageSource previewSource)
    {
        public string FileId { get; } = fileId;
        public System.Windows.Media.ImageSource PreviewSource { get; } = previewSource;
    }
}
