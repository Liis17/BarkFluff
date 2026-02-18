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