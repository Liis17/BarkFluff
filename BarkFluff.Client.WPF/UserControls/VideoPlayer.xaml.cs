using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class VideoPlayer : UserControl
    {
        #region Constants & Fields

        private DispatcherTimer? _timer;
        private bool _isUserDraggingSlider = false;
        private bool _showTimeRemaining = false;
        private double _volumeBeforeMute = 50;

        #endregion

        #region Dependency Properties

        public static readonly DependencyProperty AttachmentsProperty =
            DependencyProperty.Register(
                nameof(Attachments),
                typeof(List<AttachmentsModel>),
                typeof(VideoPlayer),
                new PropertyMetadata(null, OnAttachmentsChanged));

        public List<AttachmentsModel>? Attachments
        {
            get => (List<AttachmentsModel>?)GetValue(AttachmentsProperty);
            set => SetValue(AttachmentsProperty, value);
        }

        public static readonly DependencyProperty CurrentIndexProperty =
            DependencyProperty.Register(
                nameof(CurrentIndex),
                typeof(int),
                typeof(VideoPlayer),
                new PropertyMetadata(0, OnCurrentIndexChanged));

        public int CurrentIndex
        {
            get => (int)GetValue(CurrentIndexProperty);
            set => SetValue(CurrentIndexProperty, value);
        }

        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(
                nameof(IsOpen),
                typeof(bool),
                typeof(VideoPlayer),
                new PropertyMetadata(false, OnIsOpenChanged));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        #endregion

        #region Events

        public event EventHandler? Closed;

        #endregion

        #region Constructor

        public VideoPlayer()
        {
            InitializeComponent();

            // Таймер для обновления UI (каждые 100мс)
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += OnTimerTick;
        }

        #endregion

        #region Property Changed Handlers

        private static void OnAttachmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoPlayer viewer && viewer.IsOpen)
            {
                viewer.UpdateNavigationButtons();
            }
        }

        private static void OnCurrentIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoPlayer viewer && viewer.IsOpen)
            {
                viewer.NavigateToVideo((int)e.NewValue);
            }
        }

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoPlayer viewer)
            {
                viewer.OnIsOpenChangedImpl((bool)e.NewValue);
            }
        }

        private void OnIsOpenChangedImpl(bool isOpen)
        {
            if (isOpen)
            {
                this.Visibility = Visibility.Visible;
                var storyboard = (Storyboard)FindResource("FadeIn");
                storyboard.Begin(this);
                this.Focus();
                LoadVideoAtCurrentIndex();
                UpdateNavigationButtons();
            }
            else
            {
                var storyboard = (Storyboard)FindResource("FadeOut");
                storyboard.Completed += (s, e) =>
                {
                    this.Visibility = Visibility.Collapsed;
                    StopAndCleanup();
                };
                storyboard.Begin(this);
            }
        }

        #endregion

        #region Video Loading & Playback

        private async void LoadVideoAtCurrentIndex()
        {
            if (Attachments == null || CurrentIndex < 0 || CurrentIndex >= Attachments.Count)
                return;

            LoadingOverlay.Visibility = Visibility.Visible;
            _timer?.Stop();
            VideoElement.Stop();

            var attachment = Attachments[CurrentIndex];

            try
            {
                // Загрузить видео через FileCacheService
                var videoPath = await App.FileCacheService.GetCachedFilePathAsync(
                    attachment.FileId,
                    FileType.Video
                );

                if (FileCacheService.IsPlaceholder(videoPath))
                {
                    ShowError("Не удалось загрузить видео");
                    return;
                }

                // Установить источник и начать воспроизведение
                VideoElement.Source = new Uri(videoPath, UriKind.Absolute);
                VideoElement.Play();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка загрузки видео: {ex.Message}");
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            // Видео загружено и готово к воспроизведению
            if (VideoElement.NaturalDuration.HasTimeSpan)
            {
                var duration = VideoElement.NaturalDuration.TimeSpan;
                TimelineSlider.Maximum = duration.TotalSeconds;
                UpdateTimeDisplay();
            }

            // Применить настройки громкости
            VideoElement.Volume = VolumeSlider.Value / 100.0;

            // Запустить таймер обновления UI
            _timer?.Start();

            // Установить иконку на Pause (видео начнет играть)
            UpdatePlayPauseIcon(true);
        }

        private void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            if (LoopButton.IsChecked == true)
            {
                // Повторить с начала
                VideoElement.Position = TimeSpan.Zero;
                VideoElement.Play();
            }
            else
            {
                // Остановиться на последнем кадре (НЕ вызываем Stop())
                _timer?.Stop();
                UpdatePlayPauseIcon(false);
            }
        }

        private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            ShowError($"Ошибка воспроизведения: {e.ErrorException?.Message}");
            _timer?.Stop();
        }

        #endregion

        #region Playback Controls

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
        }

        private void TogglePlayPause()
        {
            if (VideoElement.Source == null) return;

            // Проверить, воспроизводится ли видео через CanPause
            if (VideoElement.CanPause)
            {
                VideoElement.Pause();
                _timer?.Stop();
                UpdatePlayPauseIcon(false);
            }
            else
            {
                VideoElement.Play();
                _timer?.Start();
                UpdatePlayPauseIcon(true);
            }
        }

        private void UpdatePlayPauseIcon(bool isPlaying)
        {
            PlayPauseIcon.Symbol = isPlaying
                ? Wpf.Ui.Controls.SymbolRegular.Pause24
                : Wpf.Ui.Controls.SymbolRegular.Play24;
        }

        #endregion

        #region Timeline Control

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (!_isUserDraggingSlider && VideoElement.Source != null)
            {
                if (VideoElement.NaturalDuration.HasTimeSpan)
                {
                    TimelineSlider.Value = VideoElement.Position.TotalSeconds;
                    UpdateTimeDisplay();
                }
            }
        }

        private void OnTimelineMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isUserDraggingSlider = true;
        }

        private void OnTimelineMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isUserDraggingSlider = false;
            if (VideoElement.Source != null)
            {
                VideoElement.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
            }
        }

        private void OnTimelineValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUserDraggingSlider && VideoElement.Source != null)
            {
                // Живая перемотка (ScrubbingEnabled=True делает это плавным)
                VideoElement.Position = TimeSpan.FromSeconds(e.NewValue);
                UpdateTimeDisplay();
            }
        }

        #endregion

        #region Time Display

        private void UpdateTimeDisplay()
        {
            if (!VideoElement.NaturalDuration.HasTimeSpan) return;

            var current = VideoElement.Position;
            var total = VideoElement.NaturalDuration.TimeSpan;

            if (_showTimeRemaining)
            {
                var remaining = total - current;
                TimeDisplay.Text = $"-{FormatTime(remaining)} / {FormatTime(total)}";
            }
            else
            {
                TimeDisplay.Text = $"{FormatTime(current)} / {FormatTime(total)}";
            }
        }

        private void OnTimeDisplayClick(object sender, MouseButtonEventArgs e)
        {
            _showTimeRemaining = !_showTimeRemaining;
            UpdateTimeDisplay();
        }

        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            return time.ToString(@"m\:ss");
        }

        #endregion

        #region Volume Control

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VideoElement != null)
            {
                VideoElement.Volume = e.NewValue / 100.0;
                UpdateVolumeIcon(e.NewValue);
            }
        }

        private void OnVolumeMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var delta = e.Delta > 0 ? 5 : -5;
            VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + delta, 0, 100);
            e.Handled = true;
        }

        private void OnMediaMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Скролл по видео меняет громкость
            var delta = e.Delta > 0 ? 5 : -5;
            VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + delta, 0, 100);
            e.Handled = true;
        }

        private void OnVolumeMuteToggle(object sender, RoutedEventArgs e)
        {
            if (VolumeSlider.Value > 0)
            {
                _volumeBeforeMute = VolumeSlider.Value;
                VolumeSlider.Value = 0;
            }
            else
            {
                VolumeSlider.Value = _volumeBeforeMute;
            }
        }

        private void UpdateVolumeIcon(double volume)
        {
            VolumeIcon.Symbol = volume switch
            {
                0 => Wpf.Ui.Controls.SymbolRegular.SpeakerMute24,
                < 33 => Wpf.Ui.Controls.SymbolRegular.Speaker024,
                < 66 => Wpf.Ui.Controls.SymbolRegular.Speaker124,
                _ => Wpf.Ui.Controls.SymbolRegular.Speaker224
            };
        }

        #endregion

        #region Loop Control

        private void OnLoopToggle(object sender, RoutedEventArgs e)
        {
            // ToggleButton автоматически изменяет IsChecked
        }

        private void OnToggleLoopClick(object sender, RoutedEventArgs e)
        {
            LoopButton.IsChecked = !LoopButton.IsChecked;
        }

        #endregion

        #region Context Menu

        private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
        {
            if (Attachments == null || CurrentIndex < 0 || CurrentIndex >= Attachments.Count)
                return;

            var attachment = Attachments[CurrentIndex];
            var cachedPath = App.FileCacheService.GetCachedFilePath(
                attachment.FileId,
                FileType.Video
            );

            if (FileCacheService.IsPlaceholder(cachedPath) || !File.Exists(cachedPath))
            {
                ShowError("Файл не найден в кеше");
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"/select,\"{cachedPath}\"");
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка: {ex.Message}");
            }
        }

        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            if (Attachments == null || CurrentIndex < 0 || CurrentIndex >= Attachments.Count)
                return;

            var attachment = Attachments[CurrentIndex];
            var cachedPath = App.FileCacheService.GetCachedFilePath(
                attachment.FileId,
                FileType.Video
            );

            if (!FileCacheService.IsPlaceholder(cachedPath))
            {
                Clipboard.SetText(cachedPath);
                ShowSuccess("Путь скопирован в буфер обмена");
            }
        }

        private void OnCopyFrameClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получить текущий кадр как RenderTargetBitmap
                var width = (int)VideoElement.ActualWidth;
                var height = (int)VideoElement.ActualHeight;

                if (width <= 0 || height <= 0)
                {
                    ShowError("Невозможно скопировать кадр");
                    return;
                }

                var renderBitmap = new RenderTargetBitmap(
                    width, height, 96, 96, PixelFormats.Pbgra32
                );
                renderBitmap.Render(VideoElement);

                Clipboard.SetImage(renderBitmap);
                ShowSuccess("Кадр скопирован в буфер обмена");
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка копирования кадра: {ex.Message}");
            }
        }

        #endregion

        #region Keyboard Shortcuts

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.Key)
            {
                case Key.Space:
                    TogglePlayPause();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Left:
                    OnPrevClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Right:
                    OnNextClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        #region Navigation

        private void OnPrevClick(object sender, RoutedEventArgs e)
        {
            if (CurrentIndex > 0)
            {
                CurrentIndex--;
            }
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            if (Attachments != null && CurrentIndex < Attachments.Count - 1)
            {
                CurrentIndex++;
            }
        }

        private void NavigateToVideo(int index)
        {
            if (Attachments == null || index < 0 || index >= Attachments.Count)
                return;

            LoadVideoAtCurrentIndex();
            UpdateNavigationButtons();
        }

        private void UpdateNavigationButtons()
        {
            if (Attachments == null || Attachments.Count == 0)
                return;

            PrevButton.IsEnabled = CurrentIndex > 0;
            NextButton.IsEnabled = CurrentIndex < Attachments.Count - 1;
            IndexIndicator.Text = $"{CurrentIndex + 1} / {Attachments.Count}";

            NavigationPanel.Visibility = Attachments.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        #endregion

        #region Close & Cleanup

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnBackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender)
            {
                Close();
            }
        }

        private void OnMediaContainerClick(object sender, MouseButtonEventArgs e)
        {
            // Предотвратить закрытие при клике на область видео
            e.Handled = true;
        }

        public void Close()
        {
            IsOpen = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void StopAndCleanup()
        {
            _timer?.Stop();
            VideoElement.Stop();
            VideoElement.Source = null;
            VideoElement.Close();

            TimelineSlider.Value = 0;
            TimeDisplay.Text = "00:00 / 00:00";
            UpdatePlayPauseIcon(false);
        }

        #endregion

        #region UI Helpers

        private void ShowError(string message)
        {
            App.ErideMessage?.AddMessage(message,
                new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Error
                });
        }

        private void ShowSuccess(string message)
        {
            App.ErideMessage?.AddMessage(message,
                new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Success
                });
        }

        #endregion
    }
}
