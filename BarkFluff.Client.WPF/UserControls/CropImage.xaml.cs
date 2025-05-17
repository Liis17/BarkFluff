using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для CropImage.xaml
    /// </summary>
    public partial class CropImage : UserControl
    {
        private TranslateTransform _imageTranslate = new TranslateTransform();
        private Point _lastMousePos;
        private bool _isDragging = false;

        private ScaleTransform _imageScale = new ScaleTransform(1.0, 1.0);
        private TransformGroup _transformGroup = new TransformGroup();

        public CropImage()
        {
            InitializeComponent();
            _transformGroup.Children.Add(_imageScale);
            _transformGroup.Children.Add(_imageTranslate);
            ImageControl.RenderTransform = _transformGroup;

            ImageControl.MouseLeftButtonDown += Image_MouseLeftButtonDown;
            ImageControl.MouseMove += Image_MouseMove;
            ImageControl.MouseLeftButtonUp += Image_MouseLeftButtonUp;

            ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
            LoadImage("D:\\Win11\\download\\20250505_225025.jpg");
        }

        public void LoadImage(string path)
        {
            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 600;
            bmp.EndInit();

            ImageControl.Source = bmp;
        }
        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double zoom = e.NewValue;
            _imageScale.ScaleX = zoom;
            _imageScale.ScaleY = zoom;
        }
        public BitmapSource GetCroppedAvatar()
        {
            if (ImageControl.Source is not BitmapSource bitmapSource)
                return null;

            double zoom = _imageScale.ScaleX;
            double imageLeft = _imageTranslate.X;
            double imageTop = _imageTranslate.Y;

            // Положение рамки на Canvas
            double cropLeft = Canvas.GetLeft(CropBorder);
            double cropTop = Canvas.GetTop(CropBorder);
            double cropWidth = CropBorder.Width;
            double cropHeight = CropBorder.Height;

            // Определяем, какая часть изображения попадает внутрь рамки
            double cropXOnImage = (cropLeft - imageLeft - (ImageControl.ActualWidth * (1 - zoom) / 2)) / zoom;
            double cropYOnImage = (cropTop - imageTop - (ImageControl.ActualHeight * (1 - zoom) / 2)) / zoom;
            double cropWidthOnImage = cropWidth / zoom;
            double cropHeightOnImage = cropHeight / zoom;

            // Преобразуем в пиксели оригинального изображения
            double ratioX = bitmapSource.PixelWidth / ImageControl.ActualWidth;
            double ratioY = bitmapSource.PixelHeight / ImageControl.ActualHeight;

            int x = (int)(cropXOnImage * ratioX);
            int y = (int)(cropYOnImage * ratioY);
            int width = (int)(cropWidthOnImage * ratioX);
            int height = (int)(cropHeightOnImage * ratioY);

            // Ограничения
            x = Math.Max(0, Math.Min(x, bitmapSource.PixelWidth - 1));
            y = Math.Max(0, Math.Min(y, bitmapSource.PixelHeight - 1));
            width = Math.Max(1, Math.Min(width, bitmapSource.PixelWidth - x));
            height = Math.Max(1, Math.Min(height, bitmapSource.PixelHeight - y));

            return new CroppedBitmap(bitmapSource, new Int32Rect(x, y, width, height));
        }


        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _lastMousePos = e.GetPosition(MainCanvas);
            _isDragging = true;
            ImageControl.CaptureMouse();
        }

        private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ImageControl.ReleaseMouseCapture();
        }

        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
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

            string filePath = "C:\\Users\\daske\\Desktop\\crop\\" + Guid.NewGuid().ToString() + ".png";

            // Создаем объект PngBitmapEncoder для сохранения изображения в формате PNG
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }
        }
    }
}
