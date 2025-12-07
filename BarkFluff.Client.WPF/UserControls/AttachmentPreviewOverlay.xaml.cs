using BarkFluff.Proto.Files;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для AttachmentPreviewOverlay.xaml
    /// </summary>
    public partial class AttachmentPreviewOverlay : UserControl
    {
        public event EventHandler? OnCancel;
        public event EventHandler<SendAttachmentsEventArgs>? OnSend;

        private List<AttachmentPreviewItem> _attachments = new List<AttachmentPreviewItem>();

        public AttachmentPreviewOverlay()
        {
            InitializeComponent();
        }

        public void AddAttachments(List<string> filePaths)
        {
            foreach (var filePath in filePaths)
            {
                var item = new AttachmentPreviewItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileType = DetermineFileType(filePath)
                };

                _attachments.Add(item);
                AddPreviewItem(item);
            }
        }

        public void AddImageFromClipboard(BitmapSource image)
        {
            // Save clipboard image to temp file
            var tempPath = Path.Combine(Path.GetTempPath(), $"clipboard_{Guid.NewGuid()}.png");
            using (var fileStream = new FileStream(tempPath, FileMode.Create))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fileStream);
            }

            var item = new AttachmentPreviewItem
            {
                FilePath = tempPath,
                FileName = "Изображение из буфера обмена",
                FileType = UploadFileType.MessageAttachmentImage,
                IsFromClipboard = true
            };

            _attachments.Add(item);
            AddPreviewItem(item);
        }

        private void AddPreviewItem(AttachmentPreviewItem item)
        {
            Border previewBorder = new Border
            {
                Width = 120,
                Height = 120,
                Margin = new Thickness(8),
                CornerRadius = new CornerRadius(8),
                Background = System.Windows.Media.Brushes.Black,
                Tag = item
            };

            if (item.FileType == UploadFileType.MessageAttachmentImage || 
                item.FileType == UploadFileType.MessageAttachmentGif)
            {
                // Show image preview
                var image = new Image
                {
                    Source = new BitmapImage(new Uri(item.FilePath)),
                    Stretch = System.Windows.Media.Stretch.UniformToFill
                };
                previewBorder.Child = image;
            }
            else if (item.FileType == UploadFileType.MessageAttachmentVideo)
            {
                // Show video icon with file name
                var stackPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var icon = new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = Wpf.Ui.Controls.SymbolRegular.Video24,
                    FontSize = 48,
                    Foreground = System.Windows.Media.Brushes.White
                };

                var text = new TextBlock
                {
                    Text = item.FileName,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 110,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                stackPanel.Children.Add(icon);
                stackPanel.Children.Add(text);
                previewBorder.Child = stackPanel;
            }
            else
            {
                // Show document icon with file name
                var stackPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var icon = new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = Wpf.Ui.Controls.SymbolRegular.Document24,
                    FontSize = 48,
                    Foreground = System.Windows.Media.Brushes.White
                };

                var text = new TextBlock
                {
                    Text = item.FileName,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 110,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                stackPanel.Children.Add(icon);
                stackPanel.Children.Add(text);
                previewBorder.Child = stackPanel;
            }

            PreviewItemsControl.Items.Add(previewBorder);
        }

        private UploadFileType DetermineFileType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
            var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm" };
            var gifExtensions = new[] { ".gif" };

            if (imageExtensions.Contains(extension))
                return UploadFileType.MessageAttachmentImage;
            else if (videoExtensions.Contains(extension))
                return UploadFileType.MessageAttachmentVideo;
            else if (gifExtensions.Contains(extension))
                return UploadFileType.MessageAttachmentGif;
            else
                return UploadFileType.MessageAttachmentDocument;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Clean up temp files from clipboard
            foreach (var item in _attachments.Where(a => a.IsFromClipboard))
            {
                try
                {
                    if (File.Exists(item.FilePath))
                        File.Delete(item.FilePath);
                }
                catch { }
            }

            OnCancel?.Invoke(this, EventArgs.Empty);
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var args = new SendAttachmentsEventArgs
            {
                Attachments = _attachments,
                SendSeparately = false
            };
            OnSend?.Invoke(this, args);
        }

        private void SendSeparatelyButton_Click(object sender, RoutedEventArgs e)
        {
            SendOptionsPopup.IsOpen = false;
            var args = new SendAttachmentsEventArgs
            {
                Attachments = _attachments,
                SendSeparately = true
            };
            OnSend?.Invoke(this, args);
        }

        private void SendOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            SendOptionsPopup.IsOpen = !SendOptionsPopup.IsOpen;
        }
    }

    public class AttachmentPreviewItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public UploadFileType FileType { get; set; }
        public bool IsFromClipboard { get; set; } = false;
    }

    public class SendAttachmentsEventArgs : EventArgs
    {
        public List<AttachmentPreviewItem> Attachments { get; set; } = new List<AttachmentPreviewItem>();
        public bool SendSeparately { get; set; }
    }
}
