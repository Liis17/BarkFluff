using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Control for displaying multiple images in an adaptive grid layout (Telegram-style)
    /// </summary>
    public partial class MultiImageGrid : UserControl
    {
        private const int IMAGE_MAX_WIDTH = 400;
        private const int IMAGE_MAX_HEIGHT = 300;
        private const int IMAGE_SPACING = 2;

        private List<AttachmentsModel> _attachments = new List<AttachmentsModel>();

        public MultiImageGrid()
        {
            InitializeComponent();
        }

        public void SetImages(List<AttachmentsModel> attachments)
        {
            if (attachments == null || attachments.Count == 0)
                return;

            _attachments = attachments;
            BuildImageGrid();
        }

        private void BuildImageGrid()
        {
            ImageGrid.Children.Clear();
            ImageGrid.RowDefinitions.Clear();
            ImageGrid.ColumnDefinitions.Clear();

            int count = _attachments.Count;

            if (count == 1)
            {
                // Single image - full width
                CreateSingleImageLayout();
            }
            else if (count == 2)
            {
                // Two images side by side
                CreateTwoImageLayout();
            }
            else if (count == 3)
            {
                // First image large on top, two smaller below
                CreateThreeImageLayout();
            }
            else
            {
                // 4+ images - 2xN grid
                CreateMultiImageLayout();
            }
        }

        private void CreateSingleImageLayout()
        {
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Single image - round top corners only
            var cornerRadius = new CornerRadius(18, 18, 0, 0);
            var image = CreateImageBorder(_attachments[0], IMAGE_MAX_WIDTH, IMAGE_MAX_HEIGHT, cornerRadius);
            Grid.SetRow(image, 0);
            Grid.SetColumn(image, 0);
            ImageGrid.Children.Add(image);
        }

        private void CreateTwoImageLayout()
        {
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left image - round top-left corner only
            var cornerRadiusLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT, cornerRadiusLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image1);

            // Right image - round top-right corner only
            var cornerRadiusRight = new CornerRadius(0, 18, 0, 0);
            var image2 = CreateImageBorder(_attachments[1], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT, cornerRadiusRight);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image2);
        }

        private void CreateThreeImageLayout()
        {
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // First large image on top - round top corners
            var cornerRadiusTop = new CornerRadius(18, 18, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], IMAGE_MAX_WIDTH, IMAGE_MAX_HEIGHT * 2 / 3, cornerRadiusTop);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            Grid.SetColumnSpan(image1, 2);
            image1.Margin = new Thickness(0, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            // Two smaller images below - no rounding
            var cornerRadiusNone = new CornerRadius(0);
            var image2 = CreateImageBorder(_attachments[1], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT / 3, cornerRadiusNone);
            Grid.SetRow(image2, 1);
            Grid.SetColumn(image2, 0);
            image2.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image2);

            var image3 = CreateImageBorder(_attachments[2], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT / 3, cornerRadiusNone);
            Grid.SetRow(image3, 1);
            Grid.SetColumn(image3, 1);
            image3.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image3);
        }

        private void CreateMultiImageLayout()
        {
            int count = _attachments.Count;
            int rows = (count + 1) / 2; // Ceiling division

            for (int i = 0; i < rows; i++)
            {
                ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < count; i++)
            {
                int row = i / 2;
                int col = i % 2;

                // Determine corner radius based on position
                CornerRadius cornerRadius;
                if (row == 0 && col == 0)
                {
                    // Top-left image - round top-left corner
                    cornerRadius = new CornerRadius(18, 0, 0, 0);
                }
                else if (row == 0 && col == 1)
                {
                    // Top-right image - round top-right corner
                    cornerRadius = new CornerRadius(0, 18, 0, 0);
                }
                else
                {
                    // All other images - no rounding
                    cornerRadius = new CornerRadius(0);
                }

                var image = CreateImageBorder(_attachments[i], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT / 2, cornerRadius);
                Grid.SetRow(image, row);
                Grid.SetColumn(image, col);

                // Add margins for spacing
                double marginRight = col == 0 ? IMAGE_SPACING / 2 : 0;
                double marginLeft = col == 1 ? IMAGE_SPACING / 2 : 0;
                double marginBottom = row < rows - 1 ? IMAGE_SPACING : 0;
                image.Margin = new Thickness(marginLeft, 0, marginRight, marginBottom);

                ImageGrid.Children.Add(image);
            }
        }

        private Border CreateImageBorder(AttachmentsModel attachment, int maxWidth, int maxHeight, CornerRadius cornerRadius)
        {
            var border = new Border
            {
                CornerRadius = cornerRadius,
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                ClipToBounds = true,
                Cursor = Cursors.Hand
            };

            // Create clipping geometry for rounded corners
            var clip = new RectangleGeometry
            {
                RadiusX = cornerRadius.TopLeft,
                RadiusY = cornerRadius.TopRight
            };

            // Bind the Rect to the border's actual size
            border.Loaded += (s, e) =>
            {
                clip.Rect = new Rect(0, 0, border.ActualWidth, border.ActualHeight);
            };

            border.SizeChanged += (s, e) =>
            {
                clip.Rect = new Rect(0, 0, border.ActualWidth, border.ActualHeight);
            };

            border.Clip = clip;

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
                VerticalAlignment = VerticalAlignment.Center,
                DecodePixelWidth = maxWidth
            };

            border.Child = cachedImage;

            // Add click handler
            border.MouseLeftButtonDown += (s, e) =>
            {
                OnImageClicked(fileId);
                e.Handled = true;
            };

            return border;
        }

        private void OnImageClicked(string fileId)
        {
            var msgType = new Services.Erida.MessageType
            {
                Type = Services.Erida.MessageType.MessageTypeEnum.Info
            };
            App.ErideMessage.AddMessage($"Image clicked: {fileId}", msgType);
        }
    }
}
