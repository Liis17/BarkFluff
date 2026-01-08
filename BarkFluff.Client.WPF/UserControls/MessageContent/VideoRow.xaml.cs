using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Wpf.Ui.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Represents a single row of videos in the multi-video grid
    /// Handles equal width distribution and aspect ratio maintenance
    /// </summary>
    public partial class VideoRow : UserControl
    {
        private const int VIDEO_SPACING = 2;

        private List<AttachmentsModel> _attachments = new List<AttachmentsModel>();
        private bool _isFirstRow = false;

        public VideoRow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the videos to display in this row
        /// </summary>
        /// <param name="videos">List of attachments to display</param>
        /// <param name="isFirstRow">Whether this is the first row (for corner rounding)</param>
        public void SetVideos(List<AttachmentsModel> videos, bool isFirstRow)
        {
            _attachments = videos ?? new List<AttachmentsModel>();
            _isFirstRow = isFirstRow;

            // Очистить Grid
            RowGrid.Children.Clear();
            RowGrid.ColumnDefinitions.Clear();
            RowGrid.RowDefinitions.Clear();
            RowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Создать Star-sized колонки (равномерное распределение)
            for (int i = 0; i < videos.Count; i++)
            {
                RowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Добавить видео
            for (int i = 0; i < videos.Count; i++)
            {
                var cornerRadius = DetermineCornerRadius(i, videos.Count);
                var border = CreateVideoBorder(videos[i], cornerRadius);
                Grid.SetColumn(border, i);
                border.Margin = CalculateVideoMargin(i, videos.Count);
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

            // Вычислить доступную ширину для одного видео
            int totalSpacing = (_attachments.Count - 1) * VIDEO_SPACING;
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
        /// Determines corner radius for a video at a given position
        /// Only first row gets rounded top corners
        /// </summary>
        private CornerRadius DetermineCornerRadius(int position, int totalVideos)
        {
            if (!_isFirstRow)
                return new CornerRadius(0); // Не первый ряд - без закруглений

            // Первый ряд - закруглить верхние углы
            if (totalVideos == 1)
                return new CornerRadius(18, 18, 0, 0);
            else if (position == 0)
                return new CornerRadius(18, 0, 0, 0); // Первое видео
            else if (position == totalVideos - 1)
                return new CornerRadius(0, 18, 0, 0); // Последнее видео
            else
                return new CornerRadius(0); // Средние видео
        }

        /// <summary>
        /// Calculates margin for a video to create 2px spacing between videos
        /// </summary>
        private Thickness CalculateVideoMargin(int position, int totalVideos)
        {
            if (totalVideos == 1)
                return new Thickness(0);

            // Первое видео: отступ справа
            if (position == 0)
                return new Thickness(0, 0, VIDEO_SPACING / 2.0, 0);

            // Последнее видео: отступ слева
            if (position == totalVideos - 1)
                return new Thickness(VIDEO_SPACING / 2.0, 0, 0, 0);

            // Средние видео: отступы с обеих сторон
            return new Thickness(VIDEO_SPACING / 2.0, 0, VIDEO_SPACING / 2.0, 0);
        }

        /// <summary>
        /// Creates a Border with video preview and Play overlay
        /// </summary>
        private Border CreateVideoBorder(AttachmentsModel attachment, CornerRadius cornerRadius)
        {
            var border = new Border
            {
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Background = Brushes.Black // Черный фон для видео
            };

            // Grid для наложения Preview + Play button
            var grid = new Grid();

            // Определить fileId для превью
            var fileId = !string.IsNullOrEmpty(attachment.PreviewFileId)
                ? attachment.PreviewFileId
                : attachment.FileId;

            // CachedImage для превью видео
            var cachedImage = new CachedImage
            {
                FileId = fileId,
                FileUrl = attachment.PreviewUrl,
                FileType = FileType.Video,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            grid.Children.Add(cachedImage);

            // Play button overlay (50x50, полупрозрачный черный, белая иконка)
            var playOverlay = CreatePlayButton();
            grid.Children.Add(playOverlay);

            border.Child = grid;

            // Применить Clip геометрию для закруглений
            if (cornerRadius.TopLeft > 0 || cornerRadius.TopRight > 0 ||
                cornerRadius.BottomRight > 0 || cornerRadius.BottomLeft > 0)
            {
                border.Loaded += (s, e) => UpdateClipGeometry(border, cornerRadius);
                border.SizeChanged += (s, e) => UpdateClipGeometry(border, cornerRadius);
            }

            // ВАЖНО: клик открывает ТОЛЬКО это видео (НЕ галерею)
            border.MouseLeftButtonDown += (sender, e) =>
            {
                OnVideoClick(attachment);
                e.Handled = true;
            };

            return border;
        }

        /// <summary>
        /// Creates Play button overlay (50x50, semi-transparent black background, white icon)
        /// </summary>
        private Border CreatePlayButton()
        {
            var playButton = new Border
            {
                Width = 50,
                Height = 50,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), // #80000000
                CornerRadius = new CornerRadius(25)
            };

            // SymbolIcon Play24
            var playIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.Play24,
                FontSize = 24,
                Foreground = Brushes.White
            };

            playButton.Child = playIcon;
            return playButton;
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
        /// Handles video click event
        /// КЛЮЧЕВОЕ ОТЛИЧИЕ: открывается ТОЛЬКО кликнутое видео (НЕ галерея)
        /// </summary>
        private void OnVideoClick(AttachmentsModel clickedAttachment)
        {
            // Передаем список из ОДНОГО видео с индексом 0
            OpenVideoPlayer(new List<AttachmentsModel> { clickedAttachment }, 0);
        }

        private void OpenVideoPlayer(List<AttachmentsModel> attachments, int currentIndex)
        {
            var messengerPage = FindParent<MessengerPage>(this);
            messengerPage?.OpenVideoPlayer(attachments, currentIndex);
        }

        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T ? (T)parent : FindParent<T>(parent);
        }
    }
}
