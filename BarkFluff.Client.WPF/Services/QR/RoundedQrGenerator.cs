using QRCoder;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BarkFluff.Client.WPF.Services.QR
{
    public class RoundedQrGenerator
    {
        public static void Main()
        {
            string payload = "лее мать ебать азазазазазаз"; // Ссылка внутри QR
            string logoPath = "D:\\Win11\\download\\5363848505971119737_120.jpg";    // Путь к логотипу (должен лежать рядом с exe или укажите полный путь)
            string outputPath = "C:\\Users\\daske\\Desktop\\custom_qr.png";

            // Если нет логотипа под рукой, передайте null вместо logoPath
            GenerateRoundedQr(payload, outputPath, logoPath);

            Console.WriteLine($"QR код сохранен в {outputPath}");
        }

        public static void GenerateRoundedQr(string text, string filePath, string logoPath = null)
        {
            // 1. Генерируем данные QR кода
            QRCodeGenerator qrGenerator = new QRCodeGenerator();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.H); // Высокий уровень коррекции для логотипа

            // Получаем матрицу (true = черный, false = белый)
            // ModuleMatrix - это List<BitArray>, преобразуем для удобства
            var matrix = qrCodeData.ModuleMatrix;
            int moduleCount = matrix.Count;

            // 2. Настройки рисования
            int pixelSize = 20; // Размер одного модуля (точки) в пикселях
            int padding = 40;   // Отступ белого поля вокруг
            int qrPixelWidth = moduleCount * pixelSize;
            int imgSize = qrPixelWidth + (padding * 2);

            // Настройка скругления (0.5 = круг, 0.2 = скругленный квадрат)
            float cornerRadiusRatio = 0.4f;

            using (Bitmap bitmap = new Bitmap(imgSize, imgSize))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                // Улучшаем качество графики (антиалиасинг важен для скруглений)
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Заливаем фон белым
                g.Clear(Color.White);

                // Создаем градиентную кисть (как на картинке: от светло-зеленого к темно-зеленому)
                // Координаты градиента по диагонали
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(0, 0, imgSize, imgSize),
                    Color.FromArgb(135, 206, 250), // Светлый (например голубой или лайм)
                    Color.FromArgb(34, 139, 34),   // Темный (ForestGreen)
                    45f)) // Угол 45 градусов
                {
                    // Настраиваем цвета градиента точнее под референс (Telegram зеленый)
                    ColorBlend cblend = new ColorBlend(3);
                    cblend.Colors = new Color[] { Color.FromArgb(162, 218, 104), Color.FromArgb(70, 178, 157), Color.FromArgb(41, 148, 100) };
                    cblend.Positions = new float[] { 0f, 0.5f, 1f };
                    brush.InterpolationColors = cblend;

                    // 3. Рисуем QR код
                    for (int x = 0; x < moduleCount; x++)
                    {
                        for (int y = 0; y < moduleCount; y++)
                        {
                            // Пропускаем "Глаза" (Finder Patterns), их нарисуем отдельно красиво
                            if (IsFinderPattern(x, y, moduleCount))
                                continue;

                            if (matrix[y][x]) // Обратите внимание: в QRCoder часто [y][x]
                            {
                                RectangleF rect = new RectangleF(
                                    padding + x * pixelSize,
                                    padding + y * pixelSize,
                                    pixelSize,
                                    pixelSize);

                                // Рисуем скругленный модуль (немного уменьшаем размер для эффекта разрыва, если нужно)
                                // Если хотите слитный стиль, не отнимайте padding у rect
                                float gap = 0; // Можно поставить 1-2 для зазоров
                                RectangleF drawRect = new RectangleF(rect.X + gap, rect.Y + gap, rect.Width - gap * 2, rect.Height - gap * 2);

                                FillRoundedRectangle(g, brush, drawRect, (int)(pixelSize * cornerRadiusRatio));
                            }
                        }
                    }

                    // 4. Рисуем красивые "Глаза" (Finder Patterns)
                    // Левый верхний
                    DrawFinderPattern(g, brush, padding, padding, pixelSize, cornerRadiusRatio);
                    // Правый верхний
                    DrawFinderPattern(g, brush, padding + (moduleCount - 7) * pixelSize, padding, pixelSize, cornerRadiusRatio);
                    // Левый нижний
                    DrawFinderPattern(g, brush, padding, padding + (moduleCount - 7) * pixelSize, pixelSize, cornerRadiusRatio);
                }

                // 5. Добавляем Логотип
                if (!string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath))
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

                bitmap.Save(filePath, ImageFormat.Png);
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
