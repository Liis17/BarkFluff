using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Wpf.Ui.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class AudioMessageContent : UserControl
    {
        private AttachmentsModel? _attachment;
        private bool _isCached;
        private bool _isDownloading;
        private bool _isPlaying;
        private string? _cachedFilePath;
        private readonly DispatcherTimer _positionTimer;
        private bool _isDragging;
        private Border? _trackFill;
        private Border? _thumb;

        // Singleton: только один аудио может играть одновременно
        private static AudioMessageContent? _currentlyPlaying;
        private static readonly MediaPlayer _mediaPlayer = new MediaPlayer();

        static AudioMessageContent()
        {
            _mediaPlayer.MediaEnded += (s, e) =>
            {
                _currentlyPlaying?.OnMediaEnded();
            };
            _mediaPlayer.MediaOpened += (s, e) =>
            {
                _currentlyPlaying?.OnMediaOpened();
            };
        }

        public AudioMessageContent()
        {
            InitializeComponent();

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += PositionTimer_Tick;

            ActionButton.MouseLeftButtonDown += ActionButton_Click;
            TimelineSlider.AddHandler(Slider.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(TimelineSlider_DragStarted), true);
            TimelineSlider.AddHandler(Slider.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(TimelineSlider_DragCompleted), true);
            TimelineSlider.ValueChanged += TimelineSlider_ValueChanged;
            TimelineSlider.Loaded += TimelineSlider_Loaded;
            TimelineSlider.SizeChanged += TimelineSlider_SizeChanged;
            TimelineSlider.IsVisibleChanged += TimelineSlider_IsVisibleChanged;
        }

        public AudioMessageContent(AttachmentsModel attachment) : this()
        {
            _attachment = attachment;

            // Установить оригинальное имя файла
            if (!string.IsNullOrEmpty(attachment.FileName))
            {
                FileNameText.Text = attachment.FileName;
            }
            else
            {
                FileNameText.Text = $"audio_{attachment.FileId}.mp3";
            }

            _isCached = !string.IsNullOrEmpty(attachment.FileId) &&
                        App.FileCacheService.IsFileCached(attachment.FileId);

            if (_isCached)
            {
                _cachedFilePath = App.FileCacheService.GetCachedFilePath(
                    attachment.FileId, FileType.Audio);
            }

            UpdateState();
        }

        private void UpdateState()
        {
            if (_isDownloading)
            {
                ActionIcon.Symbol = SymbolRegular.Stop24;
                DownloadProgressBar.Visibility = Visibility.Visible;
                TimelineSlider.Visibility = Visibility.Collapsed;
                ProgressPercentText.Visibility = Visibility.Visible;
                TimeDisplay.Text = "Загрузка...";
            }
            else if (_isCached)
            {
                ActionIcon.Symbol = _isPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                TimelineSlider.Visibility = Visibility.Visible;
                TimelineSlider.IsEnabled = true;
                ProgressPercentText.Visibility = Visibility.Collapsed;

                if (!_isPlaying && _mediaPlayer.NaturalDuration.HasTimeSpan && _currentlyPlaying == this)
                {
                    TimeDisplay.Text = FormatTime(_mediaPlayer.Position) + " / " +
                                       FormatTime(_mediaPlayer.NaturalDuration.TimeSpan);
                }
                else if (!_isPlaying)
                {
                    TimeDisplay.Text = "--:--";
                }
            }
            else
            {
                ActionIcon.Symbol = SymbolRegular.ArrowDownload24;
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                TimelineSlider.Visibility = Visibility.Collapsed;
                ProgressPercentText.Visibility = Visibility.Collapsed;
                TimeDisplay.Text = "--:--";
            }
        }

        private void ActionButton_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (_isDownloading)
            {
                return; // Скачивание в процессе — игнорируем
            }

            if (!_isCached)
            {
                StartDownload();
                return;
            }

            if (_isPlaying)
            {
                PauseAudio();
            }
            else
            {
                PlayAudio();
            }
        }

        private async void StartDownload()
        {
            if (_isDownloading || _attachment == null) return;
            _isDownloading = true;
            UpdateState();

            var progress = new Progress<double>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    var percent = (int)(p * 100);
                    DownloadProgressBar.Value = percent;
                    ProgressPercentText.Text = $"{percent}%";
                });
            });

            var result = await App.FileCacheService.DownloadAndCacheFileWithProgressAsync(
                _attachment.FileId, FileType.Audio, null, progress, _attachment.FileName);

            _isDownloading = false;
            _isCached = result != null;
            _cachedFilePath = result;
            UpdateState();
        }

        private void PlayAudio()
        {
            if (string.IsNullOrEmpty(_cachedFilePath)) return;

            // Останавливаем предыдущее аудио
            if (_currentlyPlaying != null && _currentlyPlaying != this)
            {
                _currentlyPlaying.StopAudio();
            }

            _currentlyPlaying = this;
            _isPlaying = true;

            var absolutePath = Path.GetFullPath(_cachedFilePath);
            _mediaPlayer.Open(new Uri(absolutePath));
            _mediaPlayer.Play();
            _positionTimer.Start();

            UpdateState();
        }

        private void PauseAudio()
        {
            _mediaPlayer.Pause();
            _isPlaying = false;
            _positionTimer.Stop();
            UpdateState();
        }

        private void StopAudio()
        {
            _mediaPlayer.Stop();
            _isPlaying = false;
            _positionTimer.Stop();
            TimelineSlider.Value = 0;
            UpdateState();
        }

        private void OnMediaOpened()
        {
            Dispatcher.Invoke(() =>
            {
                if (_mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    TimelineSlider.Maximum = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                    TimeDisplay.Text = "0:00 / " + FormatTime(_mediaPlayer.NaturalDuration.TimeSpan);
                }
            });
        }

        private void OnMediaEnded()
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _positionTimer.Stop();
                TimelineSlider.Value = 0;
                _mediaPlayer.Stop();
                UpdateState();
            });
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_isDragging || _currentlyPlaying != this) return;

            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                TimelineSlider.Value = _mediaPlayer.Position.TotalSeconds;
                TimeDisplay.Text = FormatTime(_mediaPlayer.Position) + " / " +
                                   FormatTime(_mediaPlayer.NaturalDuration.TimeSpan);
            }
        }

        private void TimelineSlider_DragStarted(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
        }

        private void TimelineSlider_DragCompleted(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            if (_currentlyPlaying == this && _mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                _mediaPlayer.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
            }
            UpdateTrackFill();
        }

        private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDragging && _currentlyPlaying == this && _mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                TimeDisplay.Text = FormatTime(TimeSpan.FromSeconds(TimelineSlider.Value)) + " / " +
                                   FormatTime(_mediaPlayer.NaturalDuration.TimeSpan);
            }
            UpdateTrackFill();
        }

        private void TimelineSlider_Loaded(object sender, RoutedEventArgs e)
        {
            TryFindTrackElements();
            UpdateTrackFill();
        }

        private void TimelineSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTrackFill();
        }

        private void TimelineSlider_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                // Когда слайдер становится видимым, применяем шаблон и ищем элементы
                TimelineSlider.ApplyTemplate();
                TimelineSlider.UpdateLayout();
                TryFindTrackElements();
                UpdateTrackFill();
            }
        }

        private void TryFindTrackElements()
        {
            _trackFill ??= FindChild<Border>(TimelineSlider, "TrackFill");
            _thumb ??= FindChild<Border>(TimelineSlider, "Thumb");
        }

        private void UpdateTrackFill()
        {
            if (_trackFill == null || _thumb == null) return;
            if (TimelineSlider.Maximum <= 0) return;

            var ratio = TimelineSlider.Value / TimelineSlider.Maximum;
            var sliderWidth = TimelineSlider.ActualWidth;
            if (sliderWidth <= 0) return;
            _trackFill.Width = Math.Max(0, ratio * sliderWidth);
            _thumb.Margin = new Thickness(Math.Max(0, ratio * sliderWidth - 6), 0, 0, 0);
        }

        private static T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && child is FrameworkElement fe && fe.Name == childName)
                    return typedChild;
                var result = FindChild<T>(child, childName);
                if (result != null) return result;
            }
            return null;
        }

        private static string FormatTime(TimeSpan time)
        {
            return time.Hours > 0
                ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
                : $"{time.Minutes}:{time.Seconds:D2}";
        }
    }
}
