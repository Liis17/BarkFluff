using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using BarkFluff.Client.WPF.Services.App.Converter;

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class VideoEditor : UserControl
    {
        private readonly FFmpegService _ffmpegService;
        private VideoMetadata? _videoMetadata;
        private VideoEditSettings _editSettings;
        private CancellationTokenSource? _cancellationTokenSource;

        // Timeline state
        private double _timelineWidth;
        private double _startTime;
        private double _endTime;
        private double _currentTime;
        private bool _isDraggingStart;
        private bool _isDraggingEnd;
        private bool _isDraggingCurrent;
        private Point _dragStartPoint;

        // Playback state
        private DispatcherTimer? _playbackTimer;
        private bool _isPlaying;

        // Resolution options
        private class ResolutionOption
        {
            public string Label { get; set; } = string.Empty;
            public int Width { get; set; }
            public int Height { get; set; }
            public object Value => new { Width, Height };
        }

        public VideoEditor(string videoPath)
        {
            InitializeComponent();

            _ffmpegService = new FFmpegService();
            _ffmpegService.ProgressChanged += OnProgressChanged;

            _editSettings = new VideoEditSettings();

            // Таймер для обновления позиции воспроизведения
            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(100);
            _playbackTimer.Tick += OnPlaybackTimerTick;

            Loaded += VideoEditor_Loaded;
            _ = LoadVideoAsync(videoPath);
        }

        private void VideoEditor_Loaded(object sender, RoutedEventArgs e)
        {
            _timelineWidth = TimelineCanvas.ActualWidth - 20; // Учитываем margin
            UpdateTimelineVisuals();
        }

        private void OnPlaybackTimerTick(object? sender, EventArgs e)
        {
            if (_videoMetadata == null || !_isPlaying) return;

            _currentTime = PreviewElement.Position.TotalSeconds;

            // Если достигли конца обрезки, остановить
            if (_currentTime >= _endTime)
            {
                _currentTime = _endTime;
                StopPlayback();
            }

            UpdateTimelineVisuals();
            UpdateTimeLabels();
        }

        #region Loading

        private async System.Threading.Tasks.Task LoadVideoAsync(string videoPath)
        {
            try
            {
                // Проверка существования файла
                if (!File.Exists(videoPath))
                {
                    ShowError("Видео файл не найден");
                    CloseEditor();
                    return;
                }

                // Проверка ffmpeg
                if (!File.Exists(@"C:\ProgramData\ffmpeg\ffmpeg.exe"))
                {
                    ShowError("FFmpeg не установлен. Установите FFmpeg в C:\\ProgramData\\ffmpeg\\");
                    CloseEditor();
                    return;
                }

                ShowLoadingOverlay("Загрузка метаданных видео...");

                // Загрузка метаданных
                _videoMetadata = await _ffmpegService.GetVideoMetadataAsync(videoPath);

                // Проверка валидности метаданных
                if (_videoMetadata.Duration <= 0)
                {
                    ShowError("Не удалось определить длительность видео");
                    CloseEditor();
                    return;
                }

                // Инициализация настроек редактирования
                _startTime = 0;
                _endTime = _videoMetadata.Duration;
                _currentTime = 0;

                _editSettings.StartTime = _startTime;
                _editSettings.EndTime = _endTime;
                _editSettings.TargetBitrate = _videoMetadata.Bitrate;
                _editSettings.TargetWidth = _videoMetadata.Width;
                _editSettings.TargetHeight = _videoMetadata.Height;

                // Инициализация UI
                InitializeTimeline();
                InitializeBitrateSlider();
                PopulateResolutionOptions();
                UpdateInfoText();

                // Загрузка превью
                try
                {
                    PreviewElement.Source = new Uri(videoPath, UriKind.Absolute);
                    PreviewElement.Position = TimeSpan.Zero;
                    PreviewElement.MediaOpened += (s, args) =>
                    {
                        // Видео загружено успешно
                        if (PreviewElement.NaturalDuration.HasTimeSpan)
                        {
                            _videoMetadata.Duration = PreviewElement.NaturalDuration.TimeSpan.TotalSeconds;
                            _endTime = _videoMetadata.Duration;
                            _editSettings.EndTime = _endTime;
                            UpdateTimelineVisuals();
                            UpdateTimeLabels();
                        }
                    };
                }
                catch (Exception ex)
                {
                    ShowWarning($"Не удалось загрузить превью: {ex.Message}");
                }

                HideLoadingOverlay();
            }
            catch (FileNotFoundException ex)
            {
                ShowError($"Файл не найден: {ex.Message}");
                CloseEditor();
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowError($"Нет доступа к файлу: {ex.Message}");
                CloseEditor();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка загрузки видео: {ex.Message}");
                App.ErideMessage?.AddMessage($"Stack trace: {ex.StackTrace}",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Debug });
                CloseEditor();
            }
        }

        #endregion

        #region Timeline

        private void InitializeTimeline()
        {
            if (_videoMetadata == null) return;

            _startTime = 0;
            _endTime = _videoMetadata.Duration;
            _currentTime = 0;

            UpdateTimelineVisuals();
            UpdateTimeLabels();
        }

        private void UpdateTimelineVisuals()
        {
            if (_videoMetadata == null || _timelineWidth <= 0) return;

            // Позиции ползунков
            double startPixels = TimeToPixels(_startTime);
            double endPixels = TimeToPixels(_endTime);
            double currentPixels = TimeToPixels(_currentTime);

            // Обновить thumbs
            Canvas.SetLeft(StartTrimThumb, startPixels - 1.5);
            Canvas.SetLeft(EndTrimThumb, endPixels - 1.5);
            Canvas.SetLeft(CurrentPositionThumb, currentPixels - 1.5);

            // Обновить выделенную область
            Canvas.SetLeft(SelectedRegion, startPixels);
            SelectedRegion.Width = endPixels - startPixels;
        }

        private void UpdateTimeLabels()
        {
            StartTimeLabel.Text = FormatTime(_startTime);
            EndTimeLabel.Text = FormatTime(_endTime);
            CurrentTimeLabel.Text = FormatTime(_currentTime);
        }

        private double TimeToPixels(double timeInSeconds)
        {
            if (_videoMetadata == null || _videoMetadata.Duration <= 0) return 0;
            return (timeInSeconds / _videoMetadata.Duration) * _timelineWidth;
        }

        private double PixelsToTime(double pixels)
        {
            if (_videoMetadata == null || _videoMetadata.Duration <= 0) return 0;
            return (pixels / _timelineWidth) * _videoMetadata.Duration;
        }

        private string FormatTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        #endregion

        #region Timeline Mouse Handlers

        private void OnStartTrimMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingStart = true;
            _dragStartPoint = e.GetPosition(TimelineCanvas);
            StartTrimThumb.CaptureMouse();
            e.Handled = true;
        }

        private void OnEndTrimMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingEnd = true;
            _dragStartPoint = e.GetPosition(TimelineCanvas);
            EndTrimThumb.CaptureMouse();
            e.Handled = true;
        }

        private void OnCurrentPositionMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingCurrent = true;
            _dragStartPoint = e.GetPosition(TimelineCanvas);
            CurrentPositionThumb.CaptureMouse();
            e.Handled = true;
        }

        private void OnThumbMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingStart = false;
            _isDraggingEnd = false;
            _isDraggingCurrent = false;

            var element = sender as UIElement;
            element?.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnStartTrimMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingStart || _videoMetadata == null) return;

            var currentPoint = e.GetPosition(TimelineCanvas);
            double newPixels = Math.Max(0, Math.Min(currentPoint.X, _timelineWidth));

            _startTime = PixelsToTime(newPixels);

            // Ограничение: не может быть больше endTime
            if (_startTime > _endTime - 0.1)
                _startTime = _endTime - 0.1;

            // Если currentTime меньше startTime, переместить к startTime
            if (_currentTime < _startTime)
                _currentTime = _startTime;

            _editSettings.StartTime = _startTime;

            UpdateTimelineVisuals();
            UpdateTimeLabels();
            UpdatePreview();
            UpdateInfoText();

            e.Handled = true;
        }

        private void OnEndTrimMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingEnd || _videoMetadata == null) return;

            var currentPoint = e.GetPosition(TimelineCanvas);
            double newPixels = Math.Max(0, Math.Min(currentPoint.X, _timelineWidth));

            _endTime = PixelsToTime(newPixels);

            // Ограничение: не может быть меньше startTime
            if (_endTime < _startTime + 0.1)
                _endTime = _startTime + 0.1;

            // Если currentTime больше endTime, переместить к endTime
            if (_currentTime > _endTime)
                _currentTime = _endTime;

            _editSettings.EndTime = _endTime;

            UpdateTimelineVisuals();
            UpdateTimeLabels();
            UpdatePreview();
            UpdateInfoText();

            e.Handled = true;
        }

        private void OnCurrentPositionMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingCurrent || _videoMetadata == null) return;

            var currentPoint = e.GetPosition(TimelineCanvas);
            double newPixels = Math.Max(0, Math.Min(currentPoint.X, _timelineWidth));

            _currentTime = PixelsToTime(newPixels);

            // Ограничение: между startTime и endTime
            _currentTime = Math.Max(_startTime, Math.Min(_currentTime, _endTime));

            UpdateTimelineVisuals();
            UpdateTimeLabels();
            UpdatePreview();

            e.Handled = true;
        }

        private void UpdatePreview()
        {
            try
            {
                PreviewElement.Position = TimeSpan.FromSeconds(_currentTime);
            }
            catch { }
        }

        #endregion

        #region Bitrate and Resolution Controls

        private void InitializeBitrateSlider()
        {
            if (_videoMetadata == null) return;

            int currentBitrate = _videoMetadata.Bitrate;

            // Максимум слайдера = текущий битрейт видео
            BitrateSlider.Maximum = currentBitrate;
            BitrateSlider.Minimum = 500;
            BitrateSlider.TickFrequency = Math.Max(100, currentBitrate / 20);
            BitrateSlider.Value = currentBitrate;

            _editSettings.TargetBitrate = currentBitrate;

            UpdateBitrateValueText();
        }

        private void UpdateBitrateSliderMaximum(int width, int height)
        {
            if (_videoMetadata == null) return;

            // Рассчитать пропорцию разрешения относительно оригинала
            double originalPixels = _videoMetadata.Width * _videoMetadata.Height;
            double targetPixels = width * height;
            double resolutionRatio = targetPixels / originalPixels;

            // Максимальный битрейт пропорционален разрешению
            // Но с учетом что при меньшем разрешении кодек работает эффективнее
            // Формула: оригинальный_битрейт * (0.4 + 0.6 * соотношение)
            // Это дает: 480p от 1080p → ~0.5x битрейт, 720p от 1080p → ~0.7x битрейт
            int maxBitrate = (int)(_videoMetadata.Bitrate * (0.4 + 0.6 * resolutionRatio));

            // Ограничения
            maxBitrate = Math.Max(500, Math.Min(maxBitrate, _videoMetadata.Bitrate));

            BitrateSlider.Maximum = maxBitrate;
            BitrateSlider.TickFrequency = Math.Max(100, maxBitrate / 20);

            // Если текущее значение больше нового максимума, установить на максимум
            if (BitrateSlider.Value > maxBitrate)
            {
                BitrateSlider.Value = maxBitrate;
            }
        }

        private void OnBitrateChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_videoMetadata == null) return;

            _editSettings.TargetBitrate = (int)BitrateSlider.Value;
            UpdateBitrateValueText();
            UpdateInfoText();
        }

        private void UpdateBitrateValueText()
        {
            BitrateValueText.Text = $"{_editSettings.TargetBitrate} kbps";
        }

        private void PopulateResolutionOptions()
        {
            if (_videoMetadata == null) return;

            int width = _videoMetadata.Width;
            int height = _videoMetadata.Height;
            double aspectRatio = (double)width / height;

            var options = new[]
            {
                new ResolutionOption
                {
                    Label = $"Оригинал ({width}x{height})",
                    Width = width,
                    Height = height
                },
                new ResolutionOption
                {
                    Label = $"{(int)(width * 0.75)}x{(int)(width * 0.75 / aspectRatio)}",
                    Width = (int)(width * 0.75),
                    Height = (int)(width * 0.75 / aspectRatio)
                },
                new ResolutionOption
                {
                    Label = $"{(int)(width * 0.5)}x{(int)(width * 0.5 / aspectRatio)}",
                    Width = (int)(width * 0.5),
                    Height = (int)(width * 0.5 / aspectRatio)
                },
                new ResolutionOption
                {
                    Label = $"{(int)(width * 0.25)}x{(int)(width * 0.25 / aspectRatio)}",
                    Width = (int)(width * 0.25),
                    Height = (int)(width * 0.25 / aspectRatio)
                }
            };

            ResolutionComboBox.ItemsSource = options;
            ResolutionComboBox.SelectedIndex = 0;
            ResolutionComboBox.SelectionChanged += (s, e) =>
            {
                if (ResolutionComboBox.SelectedItem is ResolutionOption option)
                {
                    _editSettings.TargetWidth = option.Width;
                    _editSettings.TargetHeight = option.Height;

                    // Обновить максимум слайдера битрейта при изменении разрешения
                    UpdateBitrateSliderMaximum(option.Width, option.Height);

                    UpdateInfoText();
                }
            };
        }

        #endregion

        #region Playback Controls

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                PausePlayback();
            }
            else
            {
                StartPlayback();
            }
        }

        private void StartPlayback()
        {
            if (_videoMetadata == null) return;

            _isPlaying = true;
            PreviewElement.Play();
            _playbackTimer?.Start();

            PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24;
        }

        private void PausePlayback()
        {
            _isPlaying = false;
            PreviewElement.Pause();
            _playbackTimer?.Stop();

            PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play24;
        }

        private void StopPlayback()
        {
            _isPlaying = false;
            PreviewElement.Pause();
            _playbackTimer?.Stop();

            PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play24;
        }

        #endregion

        private void UpdateInfoText()
        {
            if (_videoMetadata == null) return;

            double duration = _endTime - _startTime;
            double estimatedSizeMB = EstimateFileSize(duration, _editSettings.TargetBitrate,
                _editSettings.TargetWidth, _editSettings.TargetHeight,
                _videoMetadata.Width, _videoMetadata.Height);

            InfoText.Text = $"Исходное видео:\n" +
                            $"  Длительность: {FormatTime(_videoMetadata.Duration)}\n" +
                            $"  Разрешение: {_videoMetadata.Width}x{_videoMetadata.Height}\n" +
                            $"  Битрейт: {_videoMetadata.Bitrate} kbps\n\n" +
                            $"После обработки:\n" +
                            $"  Длительность: {FormatTime(duration)}\n" +
                            $"  Разрешение: {_editSettings.TargetWidth}x{_editSettings.TargetHeight}\n" +
                            $"  Битрейт: {_editSettings.TargetBitrate} kbps\n" +
                            $"  Примерный размер: ~{estimatedSizeMB:F1} МБ";
        }

        private double EstimateFileSize(double durationSeconds, int bitrateKbps,
            int targetWidth, int targetHeight, int originalWidth, int originalHeight)
        {
            // Базовая оценка: (битрейт видео + битрейт аудио) * длительность
            // Аудио битрейт: 128 kbps
            double videoBitrateKbps = bitrateKbps;

            // Корректировка с учетом изменения разрешения
            // При уменьшении разрешения реальный битрейт может быть ниже заданного
            double resolutionRatio = (double)(targetWidth * targetHeight) / (originalWidth * originalHeight);

            // Если разрешение уменьшается значительно, корректируем битрейт
            if (resolutionRatio < 1.0)
            {
                // Эмпирическая формула: реальный битрейт = заданный * (0.5 + 0.5 * ratio)
                // Это учитывает что при меньшем разрешении кодек сжимает эффективнее
                videoBitrateKbps *= (0.5 + 0.5 * resolutionRatio);
            }

            double totalBitrateKbps = videoBitrateKbps + 128;
            double sizeMB = (totalBitrateKbps * durationSeconds) / 8 / 1024;
            return sizeMB;
        }

        #region Video Processing

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Валидация
                if (_startTime >= _endTime)
                {
                    ShowError("Начальное время должно быть меньше конечного");
                    return;
                }

                if (_endTime - _startTime < 0.1)
                {
                    ShowError("Минимальная длительность видео - 0.1 секунды");
                    return;
                }

                // Создать директорию temp если не существует
                var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datas", "temp");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                // Сгенерировать путь выходного файла
                var outputPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.mp4");

                // Показать прогресс панель
                ShowProgressPanel();
                DisableControls();

                // Обработка
                _cancellationTokenSource = new CancellationTokenSource();
                await _ffmpegService.ProcessVideoAsync(
                    _videoMetadata!.FilePath,
                    outputPath,
                    _editSettings,
                    _cancellationTokenSource.Token
                );

                // Успешно
                HideProgressPanel();
                EnableControls(); // ИСПРАВЛЕНИЕ: разблокировать UI перед закрытием
                ShowSuccess("Видео успешно обработано!");

                // Открыть проводник с файлом
                OpenInExplorer(outputPath);

                // Небольшая задержка перед закрытием чтобы пользователь увидел сообщение
                await System.Threading.Tasks.Task.Delay(500);
                CloseEditor();
            }
            catch (OperationCanceledException)
            {
                ShowWarning("Обработка отменена");
                HideProgressPanel();
                EnableControls();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка обработки видео: {ex.Message}");
                App.ErideMessage?.AddMessage($"Stack trace: {ex.StackTrace}",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Debug });
                HideProgressPanel();
                EnableControls();
            }
        }

        private void OnProgressChanged(object? sender, ProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ProcessingProgress.Value = e.Progress;
                ProgressText.Text = $"Обработка видео... {e.Progress:F0}%";
                ProgressDetail.Text = $"{e.CurrentTime:F1}с из {e.TotalTime:F1}с";
            });
        }

        private void OpenInExplorer(string filePath)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex)
            {
                ShowWarning($"Не удалось открыть проводник: {ex.Message}");
                // Скопировать путь в буфер как fallback
                try
                {
                    Clipboard.SetText(filePath);
                    ShowInfo($"Путь к файлу скопирован в буфер обмена");
                }
                catch { }
            }
        }

        #endregion

        #region UI Helpers

        private void ShowLoadingOverlay(string message)
        {
            LoadingText.Text = message;
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void HideLoadingOverlay()
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowProgressPanel()
        {
            ProgressPanel.Visibility = Visibility.Visible;
            ButtonsPanel.Visibility = Visibility.Collapsed;
        }

        private void HideProgressPanel()
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ButtonsPanel.Visibility = Visibility.Visible;
        }

        private void DisableControls()
        {
            ControlsPanel.IsEnabled = false;
            TimelineCanvas.IsEnabled = false;
        }

        private void EnableControls()
        {
            ControlsPanel.IsEnabled = true;
            TimelineCanvas.IsEnabled = true;
        }

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

        private void ShowWarning(string message)
        {
            App.ErideMessage?.AddMessage(message,
                new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Warning
                });
        }

        private void ShowInfo(string message)
        {
            App.ErideMessage?.AddMessage(message,
                new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Info
                });
        }

        #endregion

        #region Button Handlers

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            // Если идет обработка, отменить ее
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                return;
            }

            CloseEditor();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            // Если идет обработка, предупредить пользователя
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                ShowWarning("Обработка видео в процессе. Нажмите 'Отмена' для остановки.");
                return;
            }

            CloseEditor();
        }

        private void CloseEditor()
        {
            // Остановить воспроизведение
            StopPlayback();
            _playbackTimer?.Stop();

            // Остановить превью
            try
            {
                PreviewElement.Stop();
                PreviewElement.Close();
            }
            catch { }

            // Отменить обработку если идет
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // Закрыть panel
            var parent = FindVisualParent<Pages.MessengerPage>(this);
            parent?.ClosePanel();
        }

        private T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T typedParent ? typedParent : FindVisualParent<T>(parent);
        }

        #endregion
    }
}
