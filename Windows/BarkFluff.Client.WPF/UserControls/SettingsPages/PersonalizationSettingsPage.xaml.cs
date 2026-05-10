using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using Microsoft.Win32;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    public partial class PersonalizationSettingsPage : BaseSettingsPage
    {
        public override string Title => "Персонализация";

        private static readonly SolidColorBrush AccentBrush      = new(Color.FromRgb(0xDF, 0x50, 0x00));
        private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

        private readonly List<string> _backgroundFileIds = [];
        private string _currentPosterFileId = string.Empty;
        private bool _isLoadingFromGParam;
        private bool _isInitialLoaded;

        public PersonalizationSettingsPage()
        {
            InitializeComponent();
            LoadValuesFromGParam();
        }

        public override void OnNavigatedTo()
        {
            if (!_isInitialLoaded)
            {
                _isInitialLoaded = true;
                _ = LoadFromServerAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Локальные настройки (GParam)
        // ──────────────────────────────────────────────────────────────
        private void LoadValuesFromGParam()
        {
            var gp = App.GParam;
            if (gp == null) return;

            _isLoadingFromGParam = true;
            try
            {
                CornerSlider.Value     = gp.MessageBubbleCornerRadius;
                CornerValueLabel.Text  = gp.MessageBubbleCornerRadius.ToString();

                BlurToggle.IsChecked       = gp.BackgroundBlurEnabled;
                BlurRadiusPanel.Visibility = gp.BackgroundBlurEnabled ? Visibility.Visible : Visibility.Collapsed;
                BlurRadiusSlider.Value     = gp.BackgroundBlurRadius;
                BlurRadiusLabel.Text       = gp.BackgroundBlurRadius.ToString();

                DimSlider.Value = gp.BackgroundDimPercent;
                DimLabel.Text   = $"{gp.BackgroundDimPercent}%";
            }
            finally
            {
                _isLoadingFromGParam = false;
            }
        }

        private void CornerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int val = (int)e.NewValue;
            if (CornerValueLabel != null)
                CornerValueLabel.Text = val.ToString();
            if (_isLoadingFromGParam || App.GParam == null) return;
            App.GParam.MessageBubbleCornerRadius = val;
            App.SaveGlobalParamDebounced();
        }

        private void BlurToggle_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = BlurToggle.IsChecked == true;
            BlurRadiusPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (_isLoadingFromGParam || App.GParam == null) return;
            App.GParam.BackgroundBlurEnabled = enabled;
            App.SaveGlobalParam();
            App.Messenger?.ApplyChatBackgroundSettings();
        }

        private void BlurRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int val = (int)e.NewValue;
            if (BlurRadiusLabel != null)
                BlurRadiusLabel.Text = val.ToString();
            if (_isLoadingFromGParam || App.GParam == null) return;
            App.GParam.BackgroundBlurRadius = val;
            App.SaveGlobalParamDebounced();
            App.Messenger?.ApplyChatBackgroundSettings();
        }

        private void DimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int val = (int)e.NewValue;
            if (DimLabel != null)
                DimLabel.Text = $"{val}%";
            if (_isLoadingFromGParam || App.GParam == null) return;
            App.GParam.BackgroundDimPercent = val;
            App.SaveGlobalParamDebounced();
            App.Messenger?.ApplyChatBackgroundSettings();
        }

        // ──────────────────────────────────────────────────────────────
        // Серверные данные: постер и список фонов
        // ──────────────────────────────────────────────────────────────
        private async Task LoadFromServerAsync()
        {
            await LoadPosterAsync();
            await LoadBackgroundsAsync();
        }

        private async Task LoadPosterAsync()
        {
            var gp = App.GParam;
            if (gp == null || App.ServerCommunication == null) return;

            var (error, fileId) = await App.ServerCommunication.GetProfilePoster(gp);
            if (!error.IsSuccess)
            {
                PosterStatusText.Text = error.ErrorMessage ?? string.Empty;
                return;
            }

            _currentPosterFileId = fileId ?? string.Empty;
            await ApplyPosterPreviewAsync(_currentPosterFileId);
        }

        private async Task ApplyPosterPreviewAsync(string fileId)
        {
            if (string.IsNullOrEmpty(fileId))
            {
                PosterImage.Source = null;
                PosterImage.Visibility = Visibility.Collapsed;
                PosterEmptyHint.Visibility = Visibility.Visible;
                RemovePosterBtn.IsEnabled = false;
                return;
            }

            var (urlError, url) = await App.ServerCommunication.GetFile(App.GParam, fileId);
            if (!urlError.IsSuccess || string.IsNullOrEmpty(url))
            {
                PosterStatusText.Text = $"Не удалось получить ссылку: {urlError.ErrorMessage}";
                return;
            }

            var source = await App.FileCacheService.GetCachedImageAsync(fileId, FileType.Image, url);
            PosterImage.Source = source;
            PosterImage.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
            PosterEmptyHint.Visibility = source != null ? Visibility.Collapsed : Visibility.Visible;
            RemovePosterBtn.IsEnabled = true;
        }

        private async void ChangePoster_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = false,
                Filter = "Изображения (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Все файлы (*.*)|*.*",
                Title  = "Выберите постер профиля"
            };
            if (dialog.ShowDialog() != true) return;

            ChangePosterBtn.IsEnabled = false;
            PosterStatusText.Text = "Загрузка...";
            try
            {
                var (uploadErr, fileId) = await App.ServerCommunication.UploadFileAsync(
                    App.GParam, dialog.FileName, UploadFileType.UserProfilePoster);
                if (!uploadErr.IsSuccess || string.IsNullOrEmpty(fileId))
                {
                    PosterStatusText.Text = $"Ошибка загрузки: {uploadErr.ErrorMessage}";
                    return;
                }

                var setErr = await App.ServerCommunication.SetProfilePoster(fileId, App.GParam);
                if (!setErr.IsSuccess)
                {
                    PosterStatusText.Text = $"Ошибка сохранения: {setErr.ErrorMessage}";
                    return;
                }

                _currentPosterFileId = fileId;
                await ApplyPosterPreviewAsync(fileId);
                PosterStatusText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                PosterStatusText.Text = $"Ошибка: {ex.Message}";
            }
            finally
            {
                ChangePosterBtn.IsEnabled = true;
            }
        }

        private async void RemovePoster_Click(object sender, RoutedEventArgs e)
        {
            RemovePosterBtn.IsEnabled = false;
            PosterStatusText.Text = "Удаление...";
            try
            {
                var err = await App.ServerCommunication.SetProfilePoster(string.Empty, App.GParam);
                if (!err.IsSuccess)
                {
                    PosterStatusText.Text = $"Ошибка: {err.ErrorMessage}";
                    return;
                }
                _currentPosterFileId = string.Empty;
                await ApplyPosterPreviewAsync(string.Empty);
                PosterStatusText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                PosterStatusText.Text = $"Ошибка: {ex.Message}";
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Фоны чата
        // ──────────────────────────────────────────────────────────────
        private async Task LoadBackgroundsAsync()
        {
            SetBgStatus("Загрузка...");
            BackgroundsGrid.ItemsSource = null;
            _backgroundFileIds.Clear();

            var (error, data) = await App.ServerCommunication.GetPersonalization(App.GParam);
            if (!error.IsSuccess || data == null)
            {
                SetBgStatus($"Ошибка: {error.ErrorMessage}");
                return;
            }

            _backgroundFileIds.AddRange(data.ChatBackgroundFileIds);
            SetBgStatus(string.Empty);

            await RebuildGridAsync();
        }

        private async Task RebuildGridAsync()
        {
            var items = new List<BackgroundItem>();
            var selected = App.GParam?.CurrentBackgroundFileId ?? string.Empty;

            foreach (var fileId in _backgroundFileIds)
            {
                var (err, url) = await App.ServerCommunication.GetFile(App.GParam, fileId);
                if (!err.IsSuccess || string.IsNullOrEmpty(url)) continue;

                var source = await App.FileCacheService.GetCachedImageAsync(fileId, FileType.Image, url);
                items.Add(new BackgroundItem(fileId, source, fileId == selected ? AccentBrush : TransparentBrush));
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
                    App.GParam, dialog.FileName, UploadFileType.MessageAttachmentImage);
                if (!err.IsSuccess || string.IsNullOrEmpty(fileId))
                {
                    SetBgStatus($"Ошибка загрузки: {err.ErrorMessage}");
                    return;
                }

                _backgroundFileIds.Add(fileId);
                await SaveBackgroundsAsync();
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

            // Если удалили выбранный — снять выбор
            if (App.GParam != null && App.GParam.CurrentBackgroundFileId == fileId)
            {
                App.GParam.CurrentBackgroundFileId = string.Empty;
                App.SaveGlobalParam();
                App.Messenger?.ApplyChatBackgroundSettings();
            }

            await SaveBackgroundsAsync();
            await RebuildGridAsync();
        }

        private async void Background_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string fileId) return;
            if (App.GParam == null) return;

            App.GParam.CurrentBackgroundFileId = fileId;
            App.SaveGlobalParam();
            App.Messenger?.ApplyChatBackgroundSettings();
            await RebuildGridAsync();
        }

        /// <summary>
        /// Сохраняет список фонов на сервере.
        /// Постер передаётся текущим, чтобы UpdatePersonalization не затёр его.
        /// </summary>
        private async Task SaveBackgroundsAsync()
        {
            var data = new UserPersonalizationData
            {
                ProfilePosterFileId = _currentPosterFileId
            };
            data.ChatBackgroundFileIds.AddRange(_backgroundFileIds);

            var error = await App.ServerCommunication.UpdatePersonalization(data, App.GParam);
            if (!error.IsSuccess)
                SetBgStatus($"Ошибка сохранения: {error.ErrorMessage}");
        }

        private void SetBgStatus(string msg)
            => Dispatcher.Invoke(() => BgStatusText.Text = msg);
    }

    internal sealed class BackgroundItem(string fileId, ImageSource? previewSource, Brush borderBrush)
    {
        public string FileId { get; } = fileId;
        public ImageSource? PreviewSource { get; } = previewSource;
        public Brush BorderBrush { get; } = borderBrush;
    }
}
