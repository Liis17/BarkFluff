using QRCoder;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.Services.QR
{
    public class RoundedQrGenerator
    {
        // Fallback цвета (пурпурный → розовый)
        private static readonly Color DefaultColorStart = Color.FromArgb(255, 63, 94, 251);
        private static readonly Color DefaultColorEnd = Color.FromArgb(255, 252, 70, 107);

        /// <summary>
        /// Извлекает два доминантных цвета из изображения для градиента.
        /// Возвращает средний насыщенный цвет и его hue-сдвинутый вариант.
        /// </summary>
        public static (Color colorStart, Color colorEnd) ExtractDominantColors(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    return (DefaultColorStart, DefaultColorEnd);

                using (var original = Image.FromFile(imagePath))
                using (var thumb = new Bitmap(50, 50, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(original, 0, 0, 50, 50);

                    float totalH = 0, totalS = 0, totalB = 0;
                    int count = 0;

                    for (int y = 0; y < 50; y++)
                    {
                        for (int x = 0; x < 50; x++)
                        {
                            var pixel = thumb.GetPixel(x, y);

                            // Пропускаем прозрачные, слишком тёмные, слишком светлые и ненасыщенные
                            if (pixel.A < 128) continue;
                            float brightness = pixel.GetBrightness();
                            float saturation = pixel.GetSaturation();
                            if (brightness < 0.15f || brightness > 0.85f) continue;
                            if (saturation < 0.2f) continue;

                            totalH += pixel.GetHue();
                            totalS += saturation;
                            totalB += brightness;
                            count++;
                        }
                    }

                    if (count < 50)
                        return (DefaultColorStart, DefaultColorEnd);

                    float avgH = totalH / count;
                    float avgS = totalS / count;
                    float avgB = totalB / count;

                    // Делаем цвета более насыщенными и яркими для градиента
                    float s1 = Math.Min(1f, avgS * 1.3f);
                    float b1 = Math.Clamp(avgB * 1.1f, 0.45f, 0.85f);

                    var colorStart = HslToColor(avgH, s1, b1);
                    var colorEnd = HslToColor((avgH + 35f) % 360f, s1, Math.Clamp(b1 + 0.1f, 0.45f, 0.85f));

                    return (colorStart, colorEnd);
                }
            }
            catch
            {
                return (DefaultColorStart, DefaultColorEnd);
            }
        }

        /// <summary>
        /// Конвертирует HSL в System.Drawing.Color
        /// </summary>
        private static Color HslToColor(float hue, float saturation, float lightness)
        {
            float c = (1f - Math.Abs(2f * lightness - 1f)) * saturation;
            float x = c * (1f - Math.Abs((hue / 60f) % 2f - 1f));
            float m = lightness - c / 2f;

            float r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb(255,
                (int)Math.Clamp((r + m) * 255f, 0, 255),
                (int)Math.Clamp((g + m) * 255f, 0, 255),
                (int)Math.Clamp((b + m) * 255f, 0, 255));
        }

        /// <summary>
        /// Генерирует QR код с градиентом, прозрачным фоном и скругленными модулями.
        /// </summary>
        /// <param name="text">Данные кода</param>
        /// <param name="colorStart">Начальный цвет градиента</param>
        /// <param name="colorEnd">Конечный цвет градиента</param>
        /// <param name="logoPath">Путь к картинке в центре (опционально)</param>
        /// <returns>BitmapSource для WPF</returns>
        public static BitmapSource GenerateRoundedQrBitmap(string text, Color colorStart, Color colorEnd, string logoPath = null)
        {
            // 1. Генерируем данные QR кода
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.H))
            {
                var matrix = qrCodeData.ModuleMatrix;
                int moduleCount = matrix.Count;

                // 2. Настройки размеров
                int pixelSize = 20; // Размер одного "квадратика"
                int padding = pixelSize; // Минимальный отступ (1 модуль)
                int qrPixelWidth = moduleCount * pixelSize;
                int imgSize = qrPixelWidth + (padding * 2);

                float cornerRadius = pixelSize * 0.45f; // Радиус скругления

                // Создаем Bitmap с поддержкой альфа-канала (PixelFormat.Format32bppArgb)
                Bitmap bitmap = new Bitmap(imgSize, imgSize, PixelFormat.Format32bppArgb);

                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // Устанавливаем полностью прозрачный фон
                    g.Clear(Color.Transparent);

                    // Создаем градиентную кисть на основе переданных цветов
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Rectangle(padding, padding, qrPixelWidth, qrPixelWidth),
                        colorStart,
                        colorEnd,
                        45f)) // Угол градиента 45 градусов
                    {
                        // 3. Матрица посещенных модулей для объединения в блоки
                        bool[,] visited = new bool[moduleCount, moduleCount];

                        for (int y = 0; y < moduleCount; y++)
                        {
                            for (int x = 0; x < moduleCount; x++)
                            {
                                if (visited[x, y] || !matrix[y][x])
                                    continue;

                                // Поиск горизонтальных и вертикальных блоков для объединения
                                int width = 1;
                                while (x + width < moduleCount && matrix[y][x + width] && !visited[x + width, y])
                                    width++;

                                int height = 1;
                                bool canExpand = true;
                                while (canExpand && y + height < moduleCount)
                                {
                                    for (int checkX = x; checkX < x + width; checkX++)
                                    {
                                        if (!matrix[y + height][checkX] || visited[checkX, y + height])
                                        {
                                            canExpand = false;
                                            break;
                                        }
                                    }
                                    if (canExpand) height++;
                                }

                                for (int dy = 0; dy < height; dy++)
                                    for (int dx = 0; dx < width; dx++)
                                        visited[x + dx, y + dy] = true;

                                // Рисуем объединенный блок
                                RectangleF rect = new RectangleF(
                                    padding + x * pixelSize,
                                    padding + y * pixelSize,
                                    width * pixelSize,
                                    height * pixelSize);

                                DrawModuleWithSmartCorners(g, brush, rect, cornerRadius,
                                    x, y, width, height, matrix, moduleCount);
                            }
                        }
                    }

                    // 4. Добавляем Логотип
                    if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                    {
                        try
                        {
                            using (Image logo = Image.FromFile(logoPath))
                            {
                                // Размер логотипа (чуть меньше 1/4 размера QR)
                                int logoSize = (int)(qrPixelWidth * 0.23);
                                int logoX = padding + (qrPixelWidth - logoSize) / 2;
                                int logoY = padding + (qrPixelWidth - logoSize) / 2;

                                // Рисуем чистую подложку под логотипом, чтобы QR не просвечивал
                                // Используем белый цвет, так как логотипы на прозрачности могут сливаться с градиентом
                                int bgOffset = 6;
                                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                                {
                                    g.FillEllipse(whiteBrush, logoX - bgOffset, logoY - bgOffset,
                                        logoSize + bgOffset * 2, logoSize + bgOffset * 2);
                                }

                                DrawRoundedImage(g, logo, logoX, logoY, logoSize, logoSize);
                            }
                        }
                        catch { /* Ошибка загрузки логотипа - игнорируем или логируем */ }
                    }
                }

                return ConvertBitmapToBitmapSource(bitmap);
            }
        }

        private static void DrawModuleWithSmartCorners(Graphics g, Brush brush, RectangleF rect,
            float radius, int startX, int startY, int width, int height,
            List<System.Collections.BitArray> matrix, int moduleCount)
        {
            bool hasTopLeft = HasNeighbor(matrix, moduleCount, startX - 1, startY) || HasNeighbor(matrix, moduleCount, startX, startY - 1);
            bool hasTopRight = HasNeighbor(matrix, moduleCount, startX + width, startY) || HasNeighbor(matrix, moduleCount, startX + width - 1, startY - 1);
            bool hasBottomLeft = HasNeighbor(matrix, moduleCount, startX - 1, startY + height - 1) || HasNeighbor(matrix, moduleCount, startX, startY + height);
            bool hasBottomRight = HasNeighbor(matrix, moduleCount, startX + width, startY + height - 1) || HasNeighbor(matrix, moduleCount, startX + width - 1, startY + height);

            float tl = hasTopLeft ? 0 : radius;
            float tr = hasTopRight ? 0 : radius;
            float bl = hasBottomLeft ? 0 : radius;
            float br = hasBottomRight ? 0 : radius;

            using (GraphicsPath path = GetRoundedRectanglePath(rect, tl, tr, br, bl))
            {
                g.FillPath(brush, path);
            }
        }

        private static bool HasNeighbor(List<System.Collections.BitArray> matrix, int moduleCount, int x, int y)
        {
            if (x < 0 || x >= moduleCount || y < 0 || y >= moduleCount) return false;
            return matrix[y][x];
        }

        private static GraphicsPath GetRoundedRectanglePath(RectangleF rect, float tl, float tr, float br, float bl)
        {
            GraphicsPath path = new GraphicsPath();
            float diam;

            // Top-left
            if (tl > 0) { diam = tl * 2; path.AddArc(rect.X, rect.Y, diam, diam, 180, 90); }
            else path.AddLine(rect.X, rect.Y, rect.X, rect.Y);

            // Top-right
            if (tr > 0) { diam = tr * 2; path.AddArc(rect.Right - diam, rect.Y, diam, diam, 270, 90); }
            else path.AddLine(rect.Right, rect.Y, rect.Right, rect.Y);

            // Bottom-right
            if (br > 0) { diam = br * 2; path.AddArc(rect.Right - diam, rect.Bottom - diam, diam, diam, 0, 90); }
            else path.AddLine(rect.Right, rect.Bottom, rect.Right, rect.Bottom);

            // Bottom-left
            if (bl > 0) { diam = bl * 2; path.AddArc(rect.X, rect.Bottom - diam, diam, diam, 90, 90); }
            else path.AddLine(rect.X, rect.Bottom, rect.X, rect.Bottom);

            path.CloseFigure();
            return path;
        }

        private static void DrawRoundedImage(Graphics g, Image image, int x, int y, int width, int height)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(x, y, width, height);
                Region oldRegion = g.Clip;
                g.Clip = new Region(path);
                g.DrawImage(image, x, y, width, height);
                g.Clip = oldRegion;
            }
        }

        private static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                // Важно сохранять в формате PNG для поддержки прозрачности
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Для потокобезопасности в WPF

                return bitmapImage;
            }
        }
    }
}