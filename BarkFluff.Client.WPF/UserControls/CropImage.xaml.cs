using BarkFluff.Client.WPF.Pages.SetupPages;
using BarkFluff.Client.WPF.Services.App;

using Microsoft.Win32;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Event arguments for the ImageCropped event.
    /// </summary>
    public class ImageCroppedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the cropped image.
        /// </summary>
        public BitmapSource CroppedImage { get; }

        /// <summary>
        /// Creates a new instance of ImageCroppedEventArgs.
        /// </summary>
        /// <param name="image">The cropped image.</param>
        public ImageCroppedEventArgs(BitmapSource image) => CroppedImage = image;
    }

    public partial class CropImage : UserControl
    {
        private TranslateTransform _imageTranslate = new TranslateTransform();
        private Point _lastMousePos;
        private bool _isDragging = false;

        private ScaleTransform _imageScale = new ScaleTransform(1.0, 1.0);
        private TransformGroup _transformGroup = new TransformGroup();

        private string _imagePath = string.Empty;
        private BitmapImage? _currentBitmap;

        // Zoom settings for mouse wheel
        private const double ZoomStep = 0.1;
        // Убрали фиксированный MinZoom, теперь динамический
        private const double MaxZoom = 2.5;
        private double _dynamicMinZoom = 0.5; // будет пересчитан после загрузки изображения

        // Maximum file size in bytes (50 MB)
        private const long MaxFileSize = 50 * 1024 * 1024;

        // Supported image extensions
        private static readonly string[] SupportedExtensions = { ".bmp", ".jpg", ".jpeg", ".png", ".gif", ".tiff", ".ico", ".webp" };

        /// <summary>
        /// Event raised when an image is successfully cropped.
        /// </summary>
        public event EventHandler<ImageCroppedEventArgs>? ImageCropped;

        /// <summary>
        /// Gets or sets the Pattern property for backward compatibility.
        /// </summary>
        [Obsolete("Use the ImageCropped event instead. This property will be removed in a future version.")]
        public CreateAccount? Pattern { get; set; }

        public AvatarImageHolder AvatarHolder
        {
            get => (AvatarImageHolder)GetValue(AvatarHolderProperty);
            set => SetValue(AvatarHolderProperty, value);
        }

        public static readonly DependencyProperty AvatarHolderProperty =
            DependencyProperty.Register(nameof(AvatarHolder), typeof(AvatarImageHolder), typeof(CropImage), new PropertyMetadata(null));

        public CropImage()
        {
            InitializeComponent();
            _transformGroup.Children.Add(_imageScale);
            _transformGroup.Children.Add(_imageTranslate);
            ImageControl.RenderTransform = _transformGroup;

            this.Unloaded += CropImage_Unloaded;

            ImageControl.MouseLeftButtonDown += Image_MouseLeftButtonDown;
            ImageControl.MouseMove += Image_MouseMove;
            ImageControl.MouseLeftButtonUp += Image_MouseLeftButtonUp;

            // Mouse wheel zoom support
            this.MouseWheel += CropImage_MouseWheel;

            ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;

            // Drag and Drop support
            this.Drop += CropImage_Drop;
            this.DragOver += CropImage_DragOver;

            ButtonGrid.Visibility = Visibility.Visible;
            CropGrid.Visibility = Visibility.Collapsed;

            ImageControl.SizeChanged += ImageControl_SizeChanged;
        }

        private void CropImage_Unloaded(object sender, RoutedEventArgs e)
        {
            DisposeCurrentImage();
        }

        private void ImageControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_currentBitmap != null)
                UpdateMinZoom();
        }

        #region Drag and Drop

        private void CropImage_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0 && IsValidImageFile(files[0]))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void CropImage_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    if (IsValidImageFile(filePath))
                    {
                        _imagePath = filePath;
                        LoadImage(filePath);
                    }
                    else
                    {
                        MessageBox.Show("Выбранный файл не является поддерживаемым изображением или слишком большим (макс. 50 МБ).",
                                       "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private bool IsValidImageFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            // Check file extension
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension))
                return false;

            // Check file size
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxFileSize)
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        #endregion

        #region Mouse Wheel Zoom

        private void CropImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ImageControl.Source == null)
                return;

            double currentZoom = ZoomSlider.Value;
            double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
            double minZoom = _dynamicMinZoom; // динамический минимум
            double newZoom = Math.Clamp(currentZoom + delta, minZoom, MaxZoom);

            ZoomSlider.Value = newZoom;
            e.Handled = true;
        }

        #endregion

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Выберите изображение",
                Filter = "Изображения|*.bmp;*.jpg;*.jpeg;*.png;*.gif;*.tiff;*.ico;*.webp|Все файлы|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFilePath = openFileDialog.FileName;

                // Validate file before loading
                if (!IsValidImageFile(selectedFilePath))
                {
                    MessageBox.Show("Выбранный файл не является поддерживаемым изображением или слишком большой (макс. 50 МБ).",
                                   "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _imagePath = selectedFilePath;
                LoadImage(selectedFilePath);
            }
        }

        public void LoadImage(string path)
        {
            DisposeCurrentImage();

            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = 600;
                bmp.EndInit();
                bmp.Freeze();

                _currentBitmap = bmp;
                ImageControl.Source = bmp;

                // Animate transition
                AnimateGridTransition(ButtonGrid, CropGrid);

                // После того как визуал обновится пересчитаем минимальный масштаб
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateMinZoom();
                    ResetImagePosition();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке изображения: {ex.Message}", "Ошибка",
                               MessageBoxButton.OK, MessageBoxImage.Error);

                ButtonGrid.Visibility = Visibility.Visible;
                CropGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void AnimateGridTransition(Grid fromGrid, Grid toGrid)
        {
            var fadeOutStoryboard = (Storyboard)FindResource("FadeOut");
            var fadeInStoryboard = (Storyboard)FindResource("FadeIn");

            var fadeOutClone = fadeOutStoryboard.Clone();
            var fadeInClone = fadeInStoryboard.Clone();

            fadeOutClone.Completed += (s, e) =>
            {
                fromGrid.Visibility = Visibility.Collapsed;
                fromGrid.Opacity = 1;
                toGrid.Visibility = Visibility.Visible;
                fadeInClone.Begin(toGrid);
            };

            fadeOutClone.Begin(fromGrid);
        }

        private void DisposeCurrentImage()
        {
            if (_currentBitmap != null)
            {
                ImageControl.Source = null;
                _currentBitmap = null;
                // Removed GC.Collect() anti-pattern - let the GC handle memory naturally
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_imageScale != null)
            {
                double zoom = e.NewValue;
                _imageScale.ScaleX = zoom;
                _imageScale.ScaleY = zoom;
                ConstrainImagePosition();
            }
        }

        public BitmapSource? GetCroppedAvatar()
        {
            if (ImageControl.Source is not BitmapSource bitmapSource)
                return null;

            double zoom = _imageScale.ScaleX;
            double imageLeft = _imageTranslate.X;
            double imageTop = _imageTranslate.Y;

            double cropLeft = Canvas.GetLeft(CropBorder);
            double cropTop = Canvas.GetTop(CropBorder);
            double cropWidth = CropBorder.Width;
            double cropHeight = CropBorder.Height;

            double cropXOnImage = (cropLeft - imageLeft - (ImageControl.ActualWidth * (1 - zoom) / 2)) / zoom;
            double cropYOnImage = (cropTop - imageTop - (ImageControl.ActualHeight * (1 - zoom) / 2)) / zoom;
            double cropWidthOnImage = cropWidth / zoom;
            double cropHeightOnImage = cropHeight / zoom;

            double ratioX = bitmapSource.PixelWidth / ImageControl.ActualWidth;
            double ratioY = bitmapSource.PixelHeight / ImageControl.ActualHeight;

            int x = (int)(cropXOnImage * ratioX);
            int y = (int)(cropYOnImage * ratioY);
            int width = (int)(cropWidthOnImage * ratioX);
            int height = (int)(cropHeightOnImage * ratioY);

            x = Math.Max(0, Math.Min(x, bitmapSource.PixelWidth - 1));
            y = Math.Max(0, Math.Min(y, bitmapSource.PixelHeight - 1));
            width = Math.Max(1, Math.Min(width, bitmapSource.PixelWidth - x));
            height = Math.Max(1, Math.Min(height, bitmapSource.PixelHeight - y));

            try
            {
                return new CroppedBitmap(bitmapSource, new Int32Rect(x, y, width, height));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImageControl.Source != null)
            {
                _lastMousePos = e.GetPosition(MainCanvas);
                _isDragging = true;
                ImageControl.CaptureMouse();
            }
        }

        private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ImageControl.ReleaseMouseCapture();
        }

        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && ImageControl.Source != null)
            {
                Point currentPos = e.GetPosition(MainCanvas);
                Vector delta = currentPos - _lastMousePos;
                _imageTranslate.X += delta.X;
                _imageTranslate.Y += delta.Y;
                _lastMousePos = currentPos;

                // Constrain the image to keep it within crop area bounds
                ConstrainImagePosition();
            }
        }

        /// <summary>
        /// Constrains the image position so it cannot be moved completely outside the crop area.
        /// </summary>
        private void ConstrainImagePosition()
        {
            if (ImageControl.Source == null)
                return;

            double zoom = _imageScale.ScaleX;
            double imageWidth = ImageControl.ActualWidth * zoom;
            double imageHeight = ImageControl.ActualHeight * zoom;
            double cropLeft = Canvas.GetLeft(CropBorder);
            double cropTop = Canvas.GetTop(CropBorder);
            double cropWidth = CropBorder.Width;
            double cropHeight = CropBorder.Height;
            double offsetX = ImageControl.ActualWidth * (1 - zoom) / 2;
            double offsetY = ImageControl.ActualHeight * (1 - zoom) / 2;

            // Так как minZoom гарантирует покрытие рамки, случаи imageWidth < cropWidth и imageHeight < cropHeight будут редкими, но обработаем.
            if (imageWidth < cropWidth)
                _imageTranslate.X = cropLeft + (cropWidth - imageWidth) / 2 - offsetX;
            else
                _imageTranslate.X = Math.Clamp(_imageTranslate.X, cropLeft + cropWidth - imageWidth - offsetX, cropLeft - offsetX);

            if (imageHeight < cropHeight)
                _imageTranslate.Y = cropTop + (cropHeight - imageHeight) / 2 - offsetY;
            else
                _imageTranslate.Y = Math.Clamp(_imageTranslate.Y, cropTop + cropHeight - imageHeight - offsetY, cropTop - offsetY);
        }

        private void ResetImagePosition()
        {
            if (ImageControl.Source == null)
                return;

            // Устанавливаем масштаб не ниже динамического минимума
            double startZoom = Math.Max(1.0, _dynamicMinZoom);
            if (ZoomSlider != null)
            {
                ZoomSlider.Value = startZoom;
            }
            _imageScale.ScaleX = startZoom;
            _imageScale.ScaleY = startZoom;

            // Центрируем изображение относительно области кропа
            CenterImageInCropArea();
        }

        /// <summary>
        /// Центрирует изображение относительно области кропа.
        /// </summary>
        private void CenterImageInCropArea()
        {
            if (ImageControl.Source == null)
                return;

            double zoom = _imageScale.ScaleX;
            double imageWidth = ImageControl.ActualWidth * zoom;
            double imageHeight = ImageControl.ActualHeight * zoom;

            double cropLeft = Canvas.GetLeft(CropBorder);
            double cropTop = Canvas.GetTop(CropBorder);
            double cropWidth = CropBorder.Width;
            double cropHeight = CropBorder.Height;

            // Вычисляем центр области кропа
            double cropCenterX = cropLeft + cropWidth / 2;
            double cropCenterY = cropTop + cropHeight / 2;

            // Вычисляем центр изображения (с учетом offset от ScaleTransform)
            double offsetX = ImageControl.ActualWidth * (1 - zoom) / 2;
            double offsetY = ImageControl.ActualHeight * (1 - zoom) / 2;

            // Позиционируем изображение так, чтобы его центр совпал с центром кропа
            _imageTranslate.X = cropCenterX - imageWidth / 2 - offsetX;
            _imageTranslate.Y = cropCenterY - imageHeight / 2 - offsetY;

            // Применяем ограничения, чтобы изображение не выходило за границы кропа
            ConstrainImagePosition();
        }

        /// <summary>
        /// Пересчитывает минимальный масштаб так, чтобы изображение полностью покрывало рамку обрезки.
        /// </summary>
        private void UpdateMinZoom()
        {
            if (ImageControl.Source is not BitmapSource bmp)
                return;

            double imgW = ImageControl.ActualWidth;
            double imgH = ImageControl.ActualHeight;
            if (imgW <= 0 || imgH <= 0)
                return;

            double cropW = CropBorder.Width;
            double cropH = CropBorder.Height;

            // Масштаб при котором меньшая сторона изображения ровно совпадает с соответствующей стороной рамки,
            // гарантируя что другая сторона >= рамки.
            double scaleToFit = Math.Max(cropW / imgW, cropH / imgH);

            _dynamicMinZoom = scaleToFit; // может быть >1 если изображение меньше рамки

            // Обновляем минимум слайдера
            ZoomSlider.Minimum = _dynamicMinZoom;
            if (ZoomSlider.Value < _dynamicMinZoom)
                ZoomSlider.Value = _dynamicMinZoom;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            BitmapSource? image = GetCroppedAvatar();
            if (image != null)
            {
                // Set AvatarHolder if available
                if (AvatarHolder != null)
                {
                    AvatarHolder.Image = image;
                }

                // Raise the ImageCropped event
                ImageCropped?.Invoke(this, new ImageCroppedEventArgs(image));

                // Backward compatibility: call Pattern.NextStep() if Pattern is set
#pragma warning disable CS0618 // Type or member is obsolete
                Pattern?.NextStep();
#pragma warning restore CS0618
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DisposeCurrentImage();
            ResetImagePosition();

            // Animate transition back to button grid
            AnimateGridTransition(CropGrid, ButtonGrid);
        }

        private void ResetPosition(object sender, RoutedEventArgs e)
        {
            ResetImagePosition();
        }
    }
}
