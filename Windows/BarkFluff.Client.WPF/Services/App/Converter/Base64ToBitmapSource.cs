using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.Services.App.Converter
{
    public class Base64ToBitmapSource
    {
        public static BitmapSource? ConvertBase64ToBitmapSource(string base64String)
        {
            if (string.IsNullOrEmpty(base64String))
            {
                return null;
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String);

                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    BitmapImage bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    return bitmapImage;
                }
            }
            catch (FormatException ex)
            {
                MessageBox.Show($"Ошибка формата base64: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка при преобразовании base64: {ex.Message}");
                return null;
            }
        }
    }
}
