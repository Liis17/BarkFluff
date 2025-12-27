using QRCoder;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

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

            float cornerRadius = pixelSize * 0.4f;

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

                    // 3. Создаем матрицу посещенных модулей
                    bool[,] visited = new bool[moduleCount, moduleCount];

                    // 4. Рисуем QR код с соединением соседних модулей
                    for (int y = 0; y < moduleCount; y++)
                    {
                        for (int x = 0; x < moduleCount; x++)
                        {
                            if (visited[x, y] || !matrix[y][x])
                                continue;

                            // Находим горизонтальную линию соседних модулей
                            int width = 1;
                            while (x + width < moduleCount && matrix[y][x + width] && !visited[x + width, y])
                            {
                                width++;
                            }

                            // Проверяем, можно ли расширить вниз (создать прямоугольник)
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

                            // Отмечаем все модули прямоугольника как посещенные
                            for (int dy = 0; dy < height; dy++)
                            {
                                for (int dx = 0; dx < width; dx++)
                                {
                                    visited[x + dx, y + dy] = true;
                                }
                            }

                            // Рисуем объединенный прямоугольник
                            RectangleF rect = new RectangleF(
                                padding + x * pixelSize,
                                padding + y * pixelSize,
                                width * pixelSize,
                                height * pixelSize);

                            // Определяем скругление углов в зависимости от соседей
                            DrawModuleWithSmartCorners(g, brush, rect, cornerRadius, 
                                x, y, width, height, matrix, moduleCount);
                        }
                    }
                }

                // 5. Добавляем Логотип с закруглением
                if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                {
                    using (Image logo = Image.FromFile(logoPath))
                    {
                        int logoSize = (int)(qrPixelWidth * 0.22);
                        int logoX = padding + (qrPixelWidth - logoSize) / 2;
                        int logoY = padding + (qrPixelWidth - logoSize) / 2;

                        // Рисуем белую подложку под лого (круглую)
                        int backgroundPadding = 8;
                        g.FillEllipse(Brushes.White, logoX - backgroundPadding, logoY - backgroundPadding, 
                            logoSize + backgroundPadding * 2, logoSize + backgroundPadding * 2);

                        // Рисуем логотип с круглой маской
                        DrawRoundedImage(g, logo, logoX, logoY, logoSize, logoSize);
                    }
                }

                return ConvertBitmapToBitmapSource(bitmap);
            }
        }

        /// <summary>
        /// Рисует модуль с умным скруглением углов на основе соседей
        /// </summary>
        private static void DrawModuleWithSmartCorners(Graphics g, Brush brush, RectangleF rect, 
            float radius, int startX, int startY, int width, int height, 
            List<System.Collections.BitArray> matrix, int moduleCount)
        {
            // Проверяем соседей для каждого угла
            bool hasTopLeft = HasNeighbor(matrix, moduleCount, startX - 1, startY) || 
                              HasNeighbor(matrix, moduleCount, startX, startY - 1);
            bool hasTopRight = HasNeighbor(matrix, moduleCount, startX + width, startY) || 
                               HasNeighbor(matrix, moduleCount, startX + width - 1, startY - 1);
            bool hasBottomLeft = HasNeighbor(matrix, moduleCount, startX - 1, startY + height - 1) || 
                                 HasNeighbor(matrix, moduleCount, startX, startY + height);
            bool hasBottomRight = HasNeighbor(matrix, moduleCount, startX + width, startY + height - 1) || 
                                  HasNeighbor(matrix, moduleCount, startX + width - 1, startY + height);

            float tlRadius = hasTopLeft ? 0 : radius;
            float trRadius = hasTopRight ? 0 : radius;
            float blRadius = hasBottomLeft ? 0 : radius;
            float brRadius = hasBottomRight ? 0 : radius;

            FillRoundedRectangleWithCorners(g, brush, rect, tlRadius, trRadius, brRadius, blRadius);
        }

        /// <summary>
        /// Проверяет наличие соседнего закрашенного модуля
        /// </summary>
        private static bool HasNeighbor(List<System.Collections.BitArray> matrix, int moduleCount, int x, int y)
        {
            if (x < 0 || x >= moduleCount || y < 0 || y >= moduleCount)
                return false;
            return matrix[y][x];
        }

        /// <summary>
        /// Рисует прямоугольник с индивидуальными радиусами для каждого угла
        /// </summary>
        private static void FillRoundedRectangleWithCorners(Graphics g, Brush brush, RectangleF rect, 
            float tlRadius, float trRadius, float brRadius, float blRadius)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                // Top-left corner
                if (tlRadius > 0)
                    path.AddArc(rect.X, rect.Y, tlRadius * 2, tlRadius * 2, 180, 90);
                else
                    path.AddLine(rect.X, rect.Y, rect.X, rect.Y);

                // Top edge
                path.AddLine(rect.X + tlRadius, rect.Y, rect.Right - trRadius, rect.Y);

                // Top-right corner
                if (trRadius > 0)
                    path.AddArc(rect.Right - trRadius * 2, rect.Y, trRadius * 2, trRadius * 2, 270, 90);
                else
                    path.AddLine(rect.Right, rect.Y, rect.Right, rect.Y);

                // Right edge
                path.AddLine(rect.Right, rect.Y + trRadius, rect.Right, rect.Bottom - brRadius);

                // Bottom-right corner
                if (brRadius > 0)
                    path.AddArc(rect.Right - brRadius * 2, rect.Bottom - brRadius * 2, brRadius * 2, brRadius * 2, 0, 90);
                else
                    path.AddLine(rect.Right, rect.Bottom, rect.Right, rect.Bottom);

                // Bottom edge
                path.AddLine(rect.Right - brRadius, rect.Bottom, rect.X + blRadius, rect.Bottom);

                // Bottom-left corner
                if (blRadius > 0)
                    path.AddArc(rect.X, rect.Bottom - blRadius * 2, blRadius * 2, blRadius * 2, 90, 90);
                else
                    path.AddLine(rect.X, rect.Bottom, rect.X, rect.Bottom);

                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// Рисует изображение с круглой маской
        /// </summary>
        private static void DrawRoundedImage(Graphics g, Image image, int x, int y, int width, int height)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(x, y, width, height);
                var oldClip = g.Clip;
                g.SetClip(path);
                g.DrawImage(image, new Rectangle(x, y, width, height));
                g.Clip = oldClip;
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
    }
}
