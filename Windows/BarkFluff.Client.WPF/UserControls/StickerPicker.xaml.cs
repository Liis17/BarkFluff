using BarkFluff.WebApi.Core.MessengerData;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls
{
    public class StickerSelectedEventArgs : EventArgs
    {
        public string FileId { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
    }

    public partial class StickerPicker : UserControl
    {
        public event EventHandler<StickerSelectedEventArgs>? StickerSelected;

        private bool _loaded = false;
        public bool IsEventSubscribed { get; private set; } = false;
        public void MarkEventSubscribed() => IsEventSubscribed = true;

        public StickerPicker()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Loads sticker packs from the server and builds the UI.
        /// Subsequent calls are no-ops (result is cached in memory).
        /// </summary>
        public async Task LoadAsync(GlobalParam globalParam)
        {
            if (_loaded)
                return;

            ShowLoading();

            try
            {
                var (error, packs, _) = await App.ServerCommunication.ListStickerPacksAsync(globalParam);

                if (!error.IsSuccess || packs == null || packs.Count == 0)
                {
                    ShowEmpty();
                    return;
                }

                PacksContainer.Children.Clear();

                foreach (var pack in packs)
                {
                    var (packError, _, stickers) = await App.ServerCommunication.GetStickerPackAsync(globalParam, pack.Id);

                    if (!packError.IsSuccess || stickers == null || stickers.Count == 0)
                        continue;

                    var packSection = BuildPackSection(pack, stickers);
                    PacksContainer.Children.Add(packSection);
                }

                _loaded = PacksContainer.Children.Count > 0;
                if (_loaded)
                    ShowContent();
                else
                    ShowEmpty();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StickerPicker: Failed to load sticker packs: {ex.Message}");
                ShowEmpty();
            }
        }

        private UIElement BuildPackSection(
            BarkFluff.Proto.Files.StickerPackInfo pack,
            List<BarkFluff.Proto.Files.StickerInfo> stickers)
        {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            // Pack header row: cover icon + name
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 0, 4, 6)
            };

            if (!string.IsNullOrEmpty(pack.CoverStickerId))
            {
                var coverSticker = stickers.FirstOrDefault(s => s.Id == pack.CoverStickerId);
                if (coverSticker != null)
                {
                    var coverImage = new Image
                    {
                        Width = 28,
                        Height = 28,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    LoadImageFromUrl(coverImage, coverSticker.PreviewUrl);
                    header.Children.Add(coverImage);
                }
            }

            var packNameLabel = new TextBlock
            {
                Text = pack.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily?)TryFindResource("AdwaitaSans")
                             ?? new FontFamily("Segoe UI")
            };
            header.Children.Add(packNameLabel);
            section.Children.Add(header);

            // Sticker grid (3 columns)
            var grid = new UniformGrid { Columns = 3 };

            foreach (var sticker in stickers)
            {
                var btn = BuildStickerButton(sticker);
                grid.Children.Add(btn);
            }

            section.Children.Add(grid);

            // Separator
            section.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(4, 8, 4, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            });

            return section;
        }

        private Button BuildStickerButton(BarkFluff.Proto.Files.StickerInfo sticker)
        {
            var image = new Image
            {
                Width = 86,
                Height = 86,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(3)
            };
            LoadImageFromUrl(image, sticker.PreviewUrl);

            var btn = new Button
            {
                Content = image,
                Width = 92,
                Height = 92,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = sticker
            };

            // Hover effect
            btn.MouseEnter += (s, _) =>
            {
                if (s is Button b)
                    b.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            };
            btn.MouseLeave += (s, _) =>
            {
                if (s is Button b)
                    b.Background = Brushes.Transparent;
            };

            btn.Template = CreateTransparentButtonTemplate();
            btn.Click += StickerButton_Click;
            return btn;
        }

        private void StickerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BarkFluff.Proto.Files.StickerInfo sticker)
            {
                StickerSelected?.Invoke(this, new StickerSelectedEventArgs
                {
                    FileId = sticker.FileId,
                    FileUrl = sticker.FileUrl,
                    PreviewUrl = sticker.PreviewUrl
                });
            }
        }

        private static ControlTemplate CreateTransparentButtonTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(contentFactory);

            return new ControlTemplate(typeof(Button)) { VisualTree = factory };
        }

        private static void LoadImageFromUrl(Image image, string? url)
        {
            if (string.IsNullOrEmpty(url))
                return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                    bitmap.Freeze();
                image.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StickerPicker: Failed to load image from URL '{url}': {ex.Message}");
                // Leave image empty if URL fails
            }
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;
            ContentScrollViewer.Visibility = Visibility.Collapsed;
        }

        private void ShowEmpty()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            ContentScrollViewer.Visibility = Visibility.Collapsed;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            ContentScrollViewer.Visibility = Visibility.Visible;
        }
    }
}
