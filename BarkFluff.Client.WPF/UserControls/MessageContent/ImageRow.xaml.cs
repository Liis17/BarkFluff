using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Represents a single row of images in the multi-image grid
    /// Handles equal width distribution and aspect ratio maintenance
    /// </summary>
    public partial class ImageRow : UserControl
    {
        private const int IMAGE_SPACING = 2;

        private List<AttachmentsModel> _attachments = new List<AttachmentsModel>();
        private bool _isFirstRow = false;

        public ImageRow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the images to display in this row
        /// </summary>
        /// <param name="images">List of attachments to display</param>
        /// <param name="isFirstRow">Whether this is the first row (for corner rounding)</param>
        public void SetImages(List<AttachmentsModel> images, bool isFirstRow)
        {
            _attachments = images ?? new List<AttachmentsModel>();
            _isFirstRow = isFirstRow;

            // Очистить Grid
            RowGrid.Children.Clear();
            RowGrid.ColumnDefinitions.Clear();
            RowGrid.RowDefinitions.Clear();
            RowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Создать Star-sized колонки (равномерное распределение)
            for (int i = 0; i < images.Count; i++)
            {
                RowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Добавить картинки
            for (int i = 0; i < images.Count; i++)
            {
                var cornerRadius = DetermineCornerRadius(i, images.Count);
                var border = CreateImageBorder(images[i], cornerRadius);
                Grid.SetColumn(border, i);
                border.Margin = CalculateImageMargin(i, images.Count);
                RowGrid.Children.Add(border);
            }
        }

        /// <summary>
        /// Handles size changes to maintain 16:9 aspect ratio
        /// </summary>
        private void RowGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_attachments == null || _attachments.Count == 0)
                return;

            if (RowGrid.ActualWidth <= 0)
                return;

            // Вычислить доступную ширину для одной картинки
            int totalSpacing = (_attachments.Count - 1) * IMAGE_SPACING;
            double availableWidth = (RowGrid.ActualWidth - totalSpacing) / _attachments.Count;

            // Вычислить высоту для соотношения 16:9
            double height = availableWidth * 9.0 / 16.0;

            // Применить высоту ко всем Border
            foreach (var child in RowGrid.Children)
            {
                if (child is Border border)
                {
                    border.Height = height;
                }
            }
        }

        /// <summary>
        /// Determines corner radius for an image at a given position
        /// Only first row gets rounded top corners
        /// </summary>
        private CornerRadius DetermineCornerRadius(int position, int totalImages)
        {
            if (!_isFirstRow)
                return new CornerRadius(0); // Не первый ряд - без закруглений

            // Первый ряд - закруглить верхние углы
            if (totalImages == 1)
                return new CornerRadius(18, 18, 0, 0);
            else if (position == 0)
                return new CornerRadius(18, 0, 0, 0); // Первая картинка
            else if (position == totalImages - 1)
                return new CornerRadius(0, 18, 0, 0); // Последняя картинка
            else
                return new CornerRadius(0); // Средние картинки
        }

        /// <summary>
        /// Calculates margin for an image to create 2px spacing between images
        /// </summary>
        private Thickness CalculateImageMargin(int position, int totalImages)
        {
            if (totalImages == 1)
                return new Thickness(0);

            // Первая картинка: отступ справа
            if (position == 0)
                return new Thickness(0, 0, IMAGE_SPACING / 2.0, 0);

            // Последняя картинка: отступ слева
            if (position == totalImages - 1)
                return new Thickness(IMAGE_SPACING / 2.0, 0, 0, 0);

            // Средние картинки: отступы с обеих сторон
            return new Thickness(IMAGE_SPACING / 2.0, 0, IMAGE_SPACING / 2.0, 0);
        }

        /// <summary>
        /// Creates a Border with image content
        /// </summary>
        private Border CreateImageBorder(AttachmentsModel attachment, CornerRadius cornerRadius)
        {
            var border = new Border
            {
                // НЕТ фиксированной Width! Растягивается на всю колонку
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };

            // Determine file type and file ID
            var fileType = attachment.Type == Proto.Shared.MessageAttachmentType.Gif ? FileType.Gif : FileType.Image;
            var fileId = !string.IsNullOrEmpty(attachment.PreviewFileId) ? attachment.PreviewFileId : attachment.FileId;

            // Create CachedImage control
            var cachedImage = new CachedImage
            {
                FileId = fileId,
                FileUrl = attachment.PreviewUrl,
                FileType = fileType,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center // Центрирование по центру!
            };

            border.Child = cachedImage;

            // Применить Clip геометрию для сложных закруглений
            if (cornerRadius.TopLeft > 0 || cornerRadius.TopRight > 0 ||
                cornerRadius.BottomRight > 0 || cornerRadius.BottomLeft > 0)
            {
                border.Loaded += (s, e) => UpdateClipGeometry(border, cornerRadius);
                border.SizeChanged += (s, e) => UpdateClipGeometry(border, cornerRadius);
            }

            // Add click handler
            border.MouseLeftButtonDown += (sender, e) =>
            {
                OnImageClick(fileId);
                e.Handled = true;
            };

            return border;
        }

        /// <summary>
        /// Updates the clip geometry for a border to achieve rounded corners
        /// </summary>
        private void UpdateClipGeometry(Border border, CornerRadius cornerRadius)
        {
            if (border.ActualWidth > 0 && border.ActualHeight > 0)
            {
                border.Clip = CreateRoundedRectangleGeometry(
                    border.ActualWidth,
                    border.ActualHeight,
                    cornerRadius
                );
            }
        }

        /// <summary>
        /// Creates a rounded rectangle geometry using StreamGeometry
        /// Supports different radius for each corner
        /// </summary>
        private Geometry CreateRoundedRectangleGeometry(double width, double height, CornerRadius cornerRadius)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                double topLeft = cornerRadius.TopLeft;
                double topRight = cornerRadius.TopRight;
                double bottomRight = cornerRadius.BottomRight;
                double bottomLeft = cornerRadius.BottomLeft;

                // Начинаем рисовать с точки после верхнего левого скругления
                context.BeginFigure(new Point(topLeft, 0), true, true);

                // Верхняя сторона до верхнего правого угла
                context.LineTo(new Point(width - topRight, 0), true, false);

                // Верхний правый угол
                if (topRight > 0)
                    context.ArcTo(new Point(width, topRight), new Size(topRight, topRight), 0, false, SweepDirection.Clockwise, true, false);

                // Правая сторона
                context.LineTo(new Point(width, height - bottomRight), true, false);

                // Нижний правый угол
                if (bottomRight > 0)
                    context.ArcTo(new Point(width - bottomRight, height), new Size(bottomRight, bottomRight), 0, false, SweepDirection.Clockwise, true, false);

                // Нижняя сторона
                context.LineTo(new Point(bottomLeft, height), true, false);

                // Нижний левый угол
                if (bottomLeft > 0)
                    context.ArcTo(new Point(0, height - bottomLeft), new Size(bottomLeft, bottomLeft), 0, false, SweepDirection.Clockwise, true, false);

                // Левая сторона
                context.LineTo(new Point(0, topLeft), true, false);

                // Верхний левый угол
                if (topLeft > 0)
                    context.ArcTo(new Point(topLeft, 0), new Size(topLeft, topLeft), 0, false, SweepDirection.Clockwise, true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>
        /// Handles image click event
        /// </summary>
        private void OnImageClick(string fileId)
        {
            var msgType = new Services.Erida.MessageType
            {
                Type = Services.Erida.MessageType.MessageTypeEnum.Info
            };
            App.ErideMessage.AddMessage($"Image clicked: {fileId}", msgType);
        }
    }
}
