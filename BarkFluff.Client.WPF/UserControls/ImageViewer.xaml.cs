using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Microsoft.Win32;

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

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class ImageViewer : UserControl
    {
        #region Constants
        private const double MIN_ZOOM = 0.5;
        private const double MAX_ZOOM = 5.0;
        private const double ZOOM_STEP = 0.1;
        private const double CLICK_THRESHOLD = 5.0;
        #endregion

        #region Dependency Properties

        public static readonly DependencyProperty AttachmentsProperty =
            DependencyProperty.Register(
                nameof(Attachments),
                typeof(List<AttachmentsModel>),
                typeof(ImageViewer),
                new PropertyMetadata(null, OnAttachmentsChanged));

        public List<AttachmentsModel> Attachments
        {
            get => (List<AttachmentsModel>)GetValue(AttachmentsProperty);
            set => SetValue(AttachmentsProperty, value);
        }

        public static readonly DependencyProperty CurrentIndexProperty =
            DependencyProperty.Register(
                nameof(CurrentIndex),
                typeof(int),
                typeof(ImageViewer),
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
                typeof(ImageViewer),
                new PropertyMetadata(false, OnIsOpenChanged));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        #endregion

        #region Fields

        private Point _dragStart;
        private Point _translateStart;
        private bool _isDragging = false;
        private Point _mouseDownPos;

        #endregion

        #region Events

        public event EventHandler Closed;

        #endregion

        #region Constructor

        public ImageViewer()
        {
            InitializeComponent();
            this.Loaded += ImageViewer_Loaded;
        }

        private void ImageViewer_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsOpen)
            {
                LoadImageAtCurrentIndex();
            }
        }

        #endregion

        #region Property Changed Handlers

        private static void OnAttachmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer && viewer.IsOpen)
            {
                viewer.LoadImageAtCurrentIndex();
                viewer.UpdateNavigationButtons();
            }
        }

        private static void OnCurrentIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer && viewer.IsOpen)
            {
                viewer.NavigateToImage((int)e.NewValue);
            }
        }

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer)
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
                this.Focus(); // Для обработки клавиатуры
                LoadImageAtCurrentIndex();
                UpdateNavigationButtons();
            }
            else
            {
                var storyboard = (Storyboard)FindResource("FadeOut");
                storyboard.Completed += (s, e) =>
                {
                    this.Visibility = Visibility.Collapsed;
                    CleanupResources();
                };
                storyboard.Begin(this);
            }
        }

        #endregion

        #region Zoom & Pan

        private void MainImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var delta = e.Delta > 0 ? ZOOM_STEP : -ZOOM_STEP;
            var newScale = Math.Clamp(ScaleTransform.ScaleX + delta, MIN_ZOOM, MAX_ZOOM);

            ScaleTransform.ScaleX = newScale;
            ScaleTransform.ScaleY = newScale;

            ConstrainTranslate();
            e.Handled = true;
        }

        private void MainImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(ImageContainer);

            if (ScaleTransform.ScaleX > 1.0)
            {
                _isDragging = true;
                _dragStart = _mouseDownPos;
                _translateStart = new Point(
                    TranslateTransform.X,
                    TranslateTransform.Y
                );
                MainImage.CaptureMouse();
                e.Handled = true;
            }
        }

        private void MainImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var currentPos = e.GetPosition(ImageContainer);
                var delta = currentPos - _dragStart;

                TranslateTransform.X = _translateStart.X + delta.X;
                TranslateTransform.Y = _translateStart.Y + delta.Y;

                ConstrainTranslate();
                e.Handled = true;
            }
        }

        private void MainImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                MainImage.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void ConstrainTranslate()
        {
            if (ScaleTransform.ScaleX <= 1.0)
            {
                TranslateTransform.X = 0;
                TranslateTransform.Y = 0;
                return;
            }

            if (MainImage.ActualWidth == 0 || MainImage.ActualHeight == 0)
                return;

            var scaledWidth = MainImage.ActualWidth * ScaleTransform.ScaleX;
            var scaledHeight = MainImage.ActualHeight * ScaleTransform.ScaleY;

            var maxX = (scaledWidth - MainImage.ActualWidth) / 2;
            var maxY = (scaledHeight - MainImage.ActualHeight) / 2;

            TranslateTransform.X = Math.Clamp(TranslateTransform.X, -maxX, maxX);
            TranslateTransform.Y = Math.Clamp(TranslateTransform.Y, -maxY, maxY);
        }

        private void ResetZoom()
        {
            ScaleTransform.ScaleX = 1.0;
            ScaleTransform.ScaleY = 1.0;
            TranslateTransform.X = 0;
            TranslateTransform.Y = 0;
        }

        #endregion

        #region Navigation

        private void NavigateToImage(int index)
        {
            if (Attachments == null || index < 0 || index >= Attachments.Count)
                return;

            ResetZoom();
            LoadImageAtCurrentIndex();
            UpdateNavigationButtons();
        }

        private void UpdateNavigationButtons()
        {
            if (Attachments == null || Attachments.Count == 0)
                return;

            PrevButton.IsEnabled = CurrentIndex > 0;
            NextButton.IsEnabled = CurrentIndex < Attachments.Count - 1;
            IndexIndicator.Text = $"{CurrentIndex + 1} / {Attachments.Count}";

            // Скрыть если только 1 изображение
            if (Attachments.Count <= 1)
            {
                NavigationPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                NavigationPanel.Visibility = Visibility.Visible;
            }
        }

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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.Key)
            {
                case Key.Left:
                    OnPrevClick(this, null);
                    e.Handled = true;
                    break;
                case Key.Right:
                    OnNextClick(this, null);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    OnCloseClick(this, null);
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        #region Image Loading

        private async void LoadImageAtCurrentIndex()
        {
            if (Attachments == null || CurrentIndex < 0 || CurrentIndex >= Attachments.Count)
                return;

            LoadingOverlay.Visibility = Visibility.Visible;
            var attachment = Attachments[CurrentIndex];

            try
            {
                // Показать превью сначала (если есть)
                if (!string.IsNullOrEmpty(attachment.PreviewFileId))
                {
                    var previewPath = App.FileCacheService.GetCachedFilePath(
                        attachment.PreviewFileId,
                        FileType.Image);

                    LoadImageFromPath(previewPath);
                }

                // Загрузить полную версию (FileId!)
                var fileType = attachment.Type == BarkFluff.Proto.Shared.MessageAttachmentType.Gif
                    ? FileType.Gif
                    : FileType.Image;

                // НЕ передаем providedUrl - FileCacheService сам получит URL оригинала через API
                var fullPath = await App.FileCacheService.GetCachedFilePathAsync(
                    attachment.FileId,  // ВАЖНО: используем FileId, не PreviewFileId
                    fileType
                );

                // Загрузить полную версию (или placeholder пока грузится)
                LoadImageFromPath(fullPath);
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка загрузки изображения: {ex.Message}");
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadImageFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            // Проверка: если это не pack:// URI, то это файл и нужно проверить существование
            if (!path.StartsWith("pack://") && !File.Exists(path))
                return;

            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                // Использовать RelativeOrAbsolute для поддержки pack:// URI
                bitmapImage.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                // НЕ устанавливать DecodePixelWidth - показываем полное разрешение!
                bitmapImage.EndInit();

                if (bitmapImage.CanFreeze)
                    bitmapImage.Freeze();

                MainImage.Source = bitmapImage;
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка отображения изображения: {ex.Message}");
            }
        }

        private bool IsPlaceholder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            return path.Contains("pack://application") ||
                   path.Contains("Placeholders");
        }

        #endregion

        #region Context Menu

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (Attachments == null || CurrentIndex < 0 || CurrentIndex >= Attachments.Count)
                return;

            var attachment = Attachments[CurrentIndex];
            var fileType = attachment.Type == BarkFluff.Proto.Shared.MessageAttachmentType.Gif
                ? FileType.Gif
                : FileType.Image;

            var cachedPath = App.FileCacheService.GetCachedFilePath(attachment.FileId, fileType);

            if (IsPlaceholder(cachedPath) || !File.Exists(cachedPath))
            {
                ShowError("Изображение еще не загружено");
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = attachment.FileName ?? $"image_{attachment.FileId}",
                Filter = fileType == FileType.Gif
                    ? "GIF Files (*.gif)|*.gif|All Files (*.*)|*.*"
                    : "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All Files (*.*)|*.*",
                DefaultExt = Path.GetExtension(cachedPath)
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(cachedPath, dialog.FileName, overwrite: true);
                    ShowSuccess("Изображение сохранено");
                }
                catch (Exception ex)
                {
                    ShowError($"Ошибка сохранения: {ex.Message}");
                }
            }
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainImage.Source is BitmapSource bitmapSource)
                {
                    Clipboard.SetImage(bitmapSource);
                    ShowSuccess("Изображение скопировано в буфер обмена");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка копирования: {ex.Message}");
            }
        }

        private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
        {
            if (Attachments == null || CurrentIndex < 0 || CurrentIndex >= Attachments.Count)
                return;

            var attachment = Attachments[CurrentIndex];
            var fileType = attachment.Type == BarkFluff.Proto.Shared.MessageAttachmentType.Gif
                ? FileType.Gif
                : FileType.Image;

            var cachedPath = App.FileCacheService.GetCachedFilePath(attachment.FileId, fileType);

            if (IsPlaceholder(cachedPath) || !File.Exists(cachedPath))
            {
                ShowError("Файл не найден в кеше");
                return;
            }

            try
            {
                // Открыть Explorer с выделенным файлом
                Process.Start("explorer.exe", $"/select,\"{cachedPath}\"");
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка открытия Explorer: {ex.Message}");
            }
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

        #region Close & Cleanup

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnBackgroundClick(object sender, MouseButtonEventArgs e)
        {
            // Закрыть только если клик был именно на фон, а не на дочерние элементы
            if (e.OriginalSource == sender)
            {
                Close();
            }
        }

        private void OnImageContainerClick(object sender, MouseButtonEventArgs e)
        {
            // Предотвратить всплытие до фона при клике на область изображения
            e.Handled = true;
        }

        public void Close()
        {
            IsOpen = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void CleanupResources()
        {
            // Очистить изображение
            if (MainImage.Source is BitmapImage)
            {
                MainImage.Source = null;
            }

            ResetZoom();
        }

        #endregion
    }
}
