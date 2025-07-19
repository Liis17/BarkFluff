using System.IO;
using System.Net;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.Services
{
    public class ImageColorAnalyzer
    {
        public Color GetAverageColorFromUrl(string imageUrl)
        {
            // Загрузка изображения из URL
            var bitmap = LoadBitmapFromUrl(imageUrl);

            if (bitmap == null)
                return Colors.Transparent;

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            byte[] pixelData = new byte[height * stride];
            bitmap.CopyPixels(pixelData, stride, 0);

            long totalR = 0, totalG = 0, totalB = 0;
            int pixelCount = width * height;

            for (int i = 0; i < pixelData.Length; i += 4)
            {
                byte b = pixelData[i];
                byte g = pixelData[i + 1];
                byte r = pixelData[i + 2];
                // byte a = pixelData[i + 3]; // можно учесть альфу, если нужно

                totalR += r;
                totalG += g;
                totalB += b;
            }

            byte avgR = (byte)(totalR / pixelCount);
            byte avgG = (byte)(totalG / pixelCount);
            byte avgB = (byte)(totalB / pixelCount);

            return Color.FromRgb(avgR, avgG, avgB);
        }

        private BitmapSource LoadBitmapFromUrl(string url)
        {
            using (var wc = new WebClient())
            {
                try
                {
                    byte[] imageData = wc.DownloadData(url);
                    using (var ms = new MemoryStream(imageData))
                    {
                        var decoder = BitmapDecoder.Create(
                            ms,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);

                        return decoder.Frames[0];
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
