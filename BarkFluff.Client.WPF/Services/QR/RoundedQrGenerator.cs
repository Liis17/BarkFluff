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
        /// Генерирует QR код и возвращает его как BitmapSource для использования в WPF
        /// </summary>
        /// <param name="text">Текст или URL для кодирования в QR</param>
        /// <param name="logoPath">Путь к логотипу (опционально)</param>
        /// <returns>BitmapSource с QR кодом</returns>
        public static BitmapSource GenerateRoundedQrBitmap(string text, string logoPath = null)
        {
            // 1. Генерируем данные QR кода
            QRCodeGenerator qrGenerator = new QRCodeGenerator();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.H);

            var matrix = qrCodeData.ModuleMatrix;
            int moduleCount = matrix.Count;

            // 2. Настройки рисования
            int pixelSize = 20;
            int padding = 40;
            int qrPixelWidth = moduleCount * pixelSize;
            int imgSize = qrPixelWidth + (padding * 2);

            float cornerRadiusRatio = 0.4f;

            using (Bitmap bitmap = new Bitmap(imgSize, imgSize))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                g.Clear(Color.White);

                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(0, 0, imgSize, imgSize),
                    Color.FromArgb(135, 206, 250),
                    Color.FromArgb(34, 139, 34),
                    45f))
                {
                    ColorBlend cblend = new ColorBlend(3);
                    cblend.Colors = new Color[] { Color.FromArgb(162, 218, 104), Color.FromArgb(70, 178, 157), Color.FromArgb(41, 148, 100) };
                    cblend.Positions = new float[] { 0f, 0.5f, 1f };
                    brush.InterpolationColors = cblend;

                    // 3. Рисуем QR код
                    for (int x = 0; x < moduleCount; x++)
                    {
                        for (int y = 0; y < moduleCount; y++)
                        {
                            if (IsFinderPattern(x, y, moduleCount))
                                continue;

                            if (matrix[y][x])
                            {
                                RectangleF rect = new RectangleF(
                                    padding + x * pixelSize,
                                    padding + y * pixelSize,
                                    pixelSize,
                                    pixelSize);

                                float gap = 0;
                                RectangleF drawRect = new RectangleF(rect.X + gap, rect.Y + gap, rect.Width - gap * 2, rect.Height - gap * 2);

                                FillRoundedRectangle(g, brush, drawRect, (int)(pixelSize * cornerRadiusRatio));
                            }
                        }
                    }

                    // 4. Рисуем "Глаза" (Finder Patterns)
                    // Левый верхний
                    DrawFinderPattern(g, brush, padding, padding, pixelSize, cornerRadiusRatio);
                    // Правый верхний
                    DrawFinderPattern(g, brush, padding + (moduleCount - 7) * pixelSize, padding, pixelSize, cornerRadiusRatio);
                    // Левый нижний
                    DrawFinderPattern(g, brush, padding, padding + (moduleCount - 7) * pixelSize, pixelSize, cornerRadiusRatio);
                }

                // 5. Добавляем Логотип
                if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                {
                    using (Image logo = Image.FromFile(logoPath))
                    {
                        // Логотип занимает обычно около 15-20% площади
                        int logoSize = (int)(qrPixelWidth * 0.22);
                        int logoX = padding + (qrPixelWidth - logoSize) / 2;
                        int logoY = padding + (qrPixelWidth - logoSize) / 2;

                        // Рисуем белую подложку под лого (круглую)
                        g.FillEllipse(Brushes.White, logoX - 5, logoY - 5, logoSize + 10, logoSize + 10);

                        // Рисуем сам логотип
                        g.DrawImage(logo, new Rectangle(logoX, logoY, logoSize, logoSize));
                    }
                }

                // Конвертируем в BitmapSource для WPF
                return ConvertBitmapToBitmapSource(bitmap);
            }
        }

        /// <summary>
        /// Конвертирует System.Drawing.Bitmap в WPF BitmapSource
        /// </summary>
        private static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        // Проверка: является ли точка частью "глаза" (квадраты 7x7 по углам)
        private static bool IsFinderPattern(int x, int y, int moduleCount)
        {
            // Левый верхний (0,0) - (7,7)
            if (x < 7 && y < 7) return true;
            // Правый верхний (Width-7, 0)
            if (x >= moduleCount - 7 && y < 7) return true;
            // Левый нижний (0, Height-7)
            if (x < 7 && y >= moduleCount - 7) return true;

            return false;
        }

        // Рисование одного "глаза"
        private static void DrawFinderPattern(Graphics g, Brush brush, float x, float y, int moduleSize, float cornerRatio)
        {
            // Внешняя рамка (7 модулей)
            int outerSize = moduleSize * 7;
            float outerRadius = outerSize * 0.25f; // Скругление внешней рамки
            RectangleF outerRect = new RectangleF(x, y, outerSize, outerSize);

            // Рисуем большой квадрат (залитый)
            FillRoundedRectangle(g, brush, outerRect, outerRadius);

            // Вырезаем середину белым (рисуем белый квадрат поверх, 5 модулей)
            int midSize = moduleSize * 5;
            float midRadius = midSize * 0.25f; // Чуть меньше скругление
            RectangleF midRect = new RectangleF(x + moduleSize, y + moduleSize, midSize, midSize);
            FillRoundedRectangle(g, Brushes.White, midRect, midRadius);

            // Рисуем внутренний квадрат (3 модуля)
            int innerSize = moduleSize * 3;
            float innerRadius = innerSize * 0.3f; // Скругление внутреннего
            RectangleF innerRect = new RectangleF(x + moduleSize * 2, y + moduleSize * 2, innerSize, innerSize);
            FillRoundedRectangle(g, brush, innerRect, innerRadius);
        }

        // Хелпер для рисования скругленного прямоугольника
        private static void FillRoundedRectangle(Graphics g, Brush brush, RectangleF rect, float radius)
        {
            float diameter = radius * 2;
            SizeF size = new SizeF(diameter, diameter);
            RectangleF arc = new RectangleF(rect.Location, size);
            GraphicsPath path = new GraphicsPath();

            if (radius == 0)
            {
                g.FillRectangle(brush, rect);
                return;
            }

            // Top left
            path.AddArc(arc, 180, 90);

            // Top right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
