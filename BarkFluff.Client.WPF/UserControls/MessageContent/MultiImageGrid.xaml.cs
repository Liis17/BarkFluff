using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        private const int IMAGE_SPACING = 4;

        private List<AttachmentsModel> _attachments = new List<AttachmentsModel>();
        private Dictionary<string, Image> _imageControlsByFileId = new Dictionary<string, Image>();
        private bool _isSubscribedToFileCached;

        public MultiImageGrid()
        {
            InitializeComponent();
            Loaded += MultiImageGrid_Loaded;
            Unloaded += MultiImageGrid_Unloaded;
        }

        private void MultiImageGrid_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToFileCached();
        }

        private void MultiImageGrid_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromFileCached();
            _imageControlsByFileId.Clear();
        }

        private void SubscribeToFileCached()
        {
            if (!_isSubscribedToFileCached && _imageControlsByFileId.Count > 0)
            {
                App.FileCacheService.FileCached += OnFileCached;
                _isSubscribedToFileCached = true;
            }
        }

        private void UnsubscribeFromFileCached()
        {
            if (_isSubscribedToFileCached)
            {
                App.FileCacheService.FileCached -= OnFileCached;
                _isSubscribedToFileCached = false;
            }
        }

        private void OnFileCached(string fileId, string filePath, FileType fileType)
        {
            if (_imageControlsByFileId.TryGetValue(fileId, out var imageControl))
            {
                Dispatcher.Invoke(() =>
                {
                    LoadImage(imageControl, filePath);
                    // Remove from tracking after successful load
                    _imageControlsByFileId.Remove(fileId);
                    // Unsubscribe if no more pending images
                    if (_imageControlsByFileId.Count == 0)
                    {
                        UnsubscribeFromFileCached();
                    }
                });
            }
        }

        public void SetImages(List<AttachmentsModel> attachments)
        {
            if (attachments == null || attachments.Count == 0)
                return;

            _attachments = attachments;
            _imageControlsByFileId.Clear();
            BuildImageGrid();
            // Subscribe after building grid to track pending images
            SubscribeToFileCached();
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

            var image = CreateImageBorder(_attachments[0], IMAGE_MAX_WIDTH, IMAGE_MAX_HEIGHT);
            Grid.SetRow(image, 0);
            Grid.SetColumn(image, 0);
            ImageGrid.Children.Add(image);
        }

        private void CreateTwoImageLayout()
        {
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var image1 = CreateImageBorder(_attachments[0], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image1);

            var image2 = CreateImageBorder(_attachments[1], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT);
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

            // First large image on top
            var image1 = CreateImageBorder(_attachments[0], IMAGE_MAX_WIDTH, IMAGE_MAX_HEIGHT * 2 / 3);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            Grid.SetColumnSpan(image1, 2);
            image1.Margin = new Thickness(0, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            // Two smaller images below
            var image2 = CreateImageBorder(_attachments[1], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT / 3);
            Grid.SetRow(image2, 1);
            Grid.SetColumn(image2, 0);
            image2.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image2);

            var image3 = CreateImageBorder(_attachments[2], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT / 3);
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

                var image = CreateImageBorder(_attachments[i], IMAGE_MAX_WIDTH / 2 - IMAGE_SPACING, IMAGE_MAX_HEIGHT / 2);
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

        private Border CreateImageBorder(AttachmentsModel attachment, int maxWidth, int maxHeight)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(18),
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                ClipToBounds = true
            };

            // Create clipping geometry for rounded corners
            var clip = new RectangleGeometry
            {
                RadiusX = 18,
                RadiusY = 18
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

            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Load image from cache or set placeholder
            var fileType = attachment.Type == Proto.Shared.MessageAttachmentType.Gif ? FileType.Gif : FileType.Image;
            var fileId = !string.IsNullOrEmpty(attachment.PreviewFileId) ? attachment.PreviewFileId : attachment.FileId;
            var imagePath = App.FileCacheService.GetCachedFilePath(fileId, fileType, attachment.PreviewUrl);
            
            // If this is a placeholder, track the image control for update when file is cached
            if (FileCacheService.IsPlaceholder(imagePath) && !string.IsNullOrEmpty(fileId))
            {
                _imageControlsByFileId[fileId] = image;
            }
            
            LoadImage(image, imagePath);

            border.Child = image;

            return border;
        }

        private void LoadImage(Image imageControl, string imagePath)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(imagePath, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                if (bitmapImage.CanFreeze)
                {
                    bitmapImage.Freeze();
                }
                imageControl.Source = bitmapImage;
            }
            catch
            {
                imageControl.Source = new BitmapImage(new Uri(FileCacheService.ImagePlaceholder, UriKind.RelativeOrAbsolute));
            }
        }
    }
}
