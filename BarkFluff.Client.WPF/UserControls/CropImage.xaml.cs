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

        public CropImage()
        {
            InitializeComponent();
            ImageControl.RenderTransform = _imageTranslate;
            ImageControl.MouseLeftButtonDown += Image_MouseLeftButtonDown;
            ImageControl.MouseMove += Image_MouseMove;
            ImageControl.MouseLeftButtonUp += Image_MouseLeftButtonUp;
            LoadImage("D:\\Win11\\download\\IMG_20250508_021210_475.jpg");
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

        public BitmapSource GetCroppedAvatar()
        {
            if (ImageControl.Source is not BitmapSource bitmapSource)
                return null;

            // Получаем размеры изображения на экране
            double displayedWidth = ImageControl.ActualWidth;
            double displayedHeight = ImageControl.ActualHeight;

            // Размер оригинального изображения
            double sourceWidth = bitmapSource.PixelWidth;
            double sourceHeight = bitmapSource.PixelHeight;

            // Масштаб: насколько растянуто изображение на экране
            double scaleX = sourceWidth / displayedWidth;
            double scaleY = sourceHeight / displayedHeight;

            // Позиция и размер области обрезки на Canvas
            double cropX = Canvas.GetLeft(CropBorder) - _imageTranslate.X;
            double cropY = Canvas.GetTop(CropBorder) - _imageTranslate.Y;

            // Преобразуем в координаты оригинального изображения
            int sourceX = (int)(cropX * scaleX);
            int sourceY = (int)(cropY * scaleY);
            int sourceWidthCrop = (int)(CropBorder.Width * scaleX);
            int sourceHeightCrop = (int)(CropBorder.Height * scaleY);

            // Ограничиваем область обрезки рамками изображения
            sourceX = Math.Max(0, sourceX);
            sourceY = Math.Max(0, sourceY);
            sourceWidthCrop = Math.Min(sourceWidthCrop, bitmapSource.PixelWidth - sourceX);
            sourceHeightCrop = Math.Min(sourceHeightCrop, bitmapSource.PixelHeight - sourceY);

            // Если выход за пределы — возвращаем null
            if (sourceWidthCrop <= 0 || sourceHeightCrop <= 0)
                return null;

            // Создаём обрезанный Bitmap
            return new CroppedBitmap(bitmapSource, new Int32Rect(sourceX, sourceY, sourceWidthCrop, sourceHeightCrop));
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
