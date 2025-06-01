using Microsoft.Win32;

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class CropImage : UserControl
    {
        private TranslateTransform _imageTranslate = new TranslateTransform();
        private Point _lastMousePos;
        private bool _isDragging = false;

        private ScaleTransform _imageScale = new ScaleTransform(1.0, 1.0);
        private TransformGroup _transformGroup = new TransformGroup();

        private string _imagePath = string.Empty;
        private BitmapImage _currentBitmap; // Сохраняем ссылку для освобождения

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

            ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;

            ButtonGrid.Visibility = Visibility.Visible;
            CropGrid.Visibility = Visibility.Collapsed;
        }

        private void CropImage_Unloaded(object sender, RoutedEventArgs e)
        {
            DisposeCurrentImage();
        }

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
                _imagePath = selectedFilePath;
                LoadImage(selectedFilePath);
            }
        }

        public void LoadImage(string path)
        {
            DisposeCurrentImage();

            ButtonGrid.Visibility = Visibility.Collapsed;
            CropGrid.Visibility = Visibility.Visible;

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

                ResetImagePosition();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке изображения: {ex.Message}", "Ошибка",
                               MessageBoxButton.OK, MessageBoxImage.Error);

                ButtonGrid.Visibility = Visibility.Visible;
                CropGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void DisposeCurrentImage()
        {
            if (_currentBitmap != null)
            {
                ImageControl.Source = null;

                _currentBitmap = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_imageScale != null)
            {
                double zoom = e.NewValue;
                _imageScale.ScaleX = zoom;
                _imageScale.ScaleY = zoom;
            }
        }

        public BitmapSource GetCroppedAvatar()
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
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            BitmapSource image = GetCroppedAvatar();
            if (image != null)
            {
                // Здесь код для обработки изображения
            }
        }

        private void ResetPosition(object sender, RoutedEventArgs e)
        {
            ResetImagePosition();
        }

        private void ResetImagePosition()
        {
            _imageTranslate.X = 0;
            _imageTranslate.Y = 0;
            _imageScale.ScaleX = 1.0;
            _imageScale.ScaleY = 1.0;
            if (ZoomSlider != null)
            {
                ZoomSlider.Value = 1.0;
            }
        }


    }
}