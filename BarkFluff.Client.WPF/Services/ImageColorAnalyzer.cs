using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.Services
{
    public class ImageColorAnalyzer
    {
        public async Task<Color> GetAverageColorFromUrlAsync(string imageUrl)
        {
            var bitmap = await LoadBitmapFromUrlAsync(imageUrl);
            if (bitmap == null)
            {
                Console.WriteLine("Не удалось загрузить изображение");
                return Colors.Transparent;
            }

            // Проверка формата и конвертация, если нужно
            BitmapSource source = bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
            {
                Console.WriteLine($"Конвертация формата: {bitmap.Format} -> Bgra32");
                source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            }

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * 4;
            byte[] pixelData = new byte[height * stride];
            source.CopyPixels(pixelData, stride, 0);

            long totalR = 0, totalG = 0, totalB = 0;
            int pixelCount = 0; // Учитываем только непрозрачные пиксели

            for (int i = 0; i < pixelData.Length; i += 4)
            {
                byte a = pixelData[i + 3]; // Альфа-канал
                if (a == 0) continue; // Пропускаем прозрачные пиксели
                byte b = pixelData[i];
                byte g = pixelData[i + 1];
                byte r = pixelData[i + 2];
                totalR += r;
                totalG += g;
                totalB += b;
                pixelCount++;
            }

            if (pixelCount == 0)
            {
                Console.WriteLine("Нет непрозрачных пикселей");
                return Colors.Transparent;
            }

            byte avgR = (byte)(totalR / pixelCount);
            byte avgG = (byte)(totalG / pixelCount);
            byte avgB = (byte)(totalB / pixelCount);

            Console.WriteLine($"URL: {imageUrl}, Цвет: {avgR}, {avgG}, {avgB}, Пикселей: {pixelCount}");
            return Color.FromRgb(avgR, avgG, avgB);
        }

        private async Task<BitmapSource> LoadBitmapFromUrlAsync(string url)
        {
            using (var client = new HttpClient())
            {
                try
                {
                    byte[] imageData = await client.GetByteArrayAsync(url);
                    Console.WriteLine($"Загружено {imageData.Length} байт с {url}");
                    using (var ms = new MemoryStream(imageData))
                    {
                        var decoder = BitmapDecoder.Create(
                            ms,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        return decoder.Frames[0];
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка загрузки: {ex.Message}");
                    return null;
                }
            }
        }
    }
}
