using BarkFluff.Proto.Files;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        private const int MaxAttachments = 10;
        private bool _hasNonImageFiles = false;

        public AttachmentPreviewOverlay()
        {
            InitializeComponent();
        }

        public void AddAttachments(List<string> filePaths)
        {
            bool added = false;
            foreach (var filePath in filePaths)
            {
                if (_attachments.Count >= MaxAttachments) break;

                // Check for duplicates
                if (_attachments.Any(a => a.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var item = new AttachmentPreviewItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileType = DetermineFileType(filePath)
                };

                _attachments.Add(item);
                added = true;
            }

            if (added)
            {
                RefreshUI();
            }
        }

        public void AddImageFromClipboard(BitmapSource image)
        {
            if (_attachments.Count >= MaxAttachments) return;

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
            RefreshUI();
        }

        private void RefreshUI()
        {
            PreviewItemsControl.Items.Clear();

            foreach (var item in _attachments)
            {
                AddPreviewItem(item);
            }

            UpdateHeader();
            UpdateSendAsFileCheckbox();

            if (_attachments.Count < MaxAttachments)
            {
                AddPlusButton();
            }
        }

        private void UpdateSendAsFileCheckbox()
        {
            // Check if there are any non-image files
            _hasNonImageFiles = _attachments.Any(a => 
                a.FileType == UploadFileType.MessageAttachmentDocument || 
                a.FileType == UploadFileType.MessageAttachmentVideo);

            // Check if there are any images
            bool hasImages = _attachments.Any(a => 
                a.FileType == UploadFileType.MessageAttachmentImage || 
                a.FileType == UploadFileType.MessageAttachmentGif);

            if (hasImages)
            {
                SendAsFileCheckBox.Visibility = Visibility.Visible;
                
                if (_hasNonImageFiles)
                {
                    // Force send as files when mixed content
                    SendAsFileCheckBox.IsChecked = true;
                    SendAsFileCheckBox.IsEnabled = false;
                }
                else
                {
                    SendAsFileCheckBox.IsEnabled = true;
                }
            }
            else
            {
                // No images, hide checkbox
                SendAsFileCheckBox.Visibility = Visibility.Collapsed;
            }
        }

        private void SendAsFileCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Update file types if sending as files
            if (SendAsFileCheckBox.IsChecked == true)
            {
                foreach (var item in _attachments)
                {
                    if (item.FileType == UploadFileType.MessageAttachmentImage || 
                        item.FileType == UploadFileType.MessageAttachmentGif)
                    {
                        item.OriginalFileType = item.FileType;
                        item.FileType = UploadFileType.MessageAttachmentDocument;
                    }
                }
            }
            else
            {
                // Restore original file types
                foreach (var item in _attachments)
                {
                    if (item.OriginalFileType.HasValue)
                    {
                        item.FileType = item.OriginalFileType.Value;
                        item.OriginalFileType = null;
                    }
                }
            }
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            if (_attachments.Count == 0)
            {
                HeaderTextBlock.Text = "Предпросмотр вложений";
                return;
            }

            bool allImages = _attachments.All(a => a.FileType == UploadFileType.MessageAttachmentImage || a.FileType == UploadFileType.MessageAttachmentGif);

            if (allImages)
            {
                HeaderTextBlock.Text = $"Отправить {_attachments.Count} фото";
            }
            else
            {
                string suffix = "файлов";
                int count = _attachments.Count;
                
                if (count == 1) suffix = "файл";
                else if (count >= 2 && count <= 4) suffix = "файла";
                
                HeaderTextBlock.Text = $"Отправить {count} {suffix}";
            }
        }

        private void AddPlusButton()
        {
            var button = new Button
            {
                Width = 100,
                Height = 156,
                Margin = new Thickness(4),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            string templateXaml = @"
                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
                    <Border Background='{TemplateBinding Background}' 
                            BorderBrush='{TemplateBinding BorderBrush}' 
                            BorderThickness='{TemplateBinding BorderThickness}' 
                            CornerRadius='8'>
                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    </Border>
                </ControlTemplate>";

            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(templateXaml);

            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Add24,
                FontSize = 32,
                Foreground = System.Windows.Media.Brushes.White
            };

            button.Content = icon;

            button.Click += (s, e) =>
            {
                var openFileDialog = new OpenFileDialog
                {
                    Multiselect = true,
                    Filter = "All files (*.*)|*.*"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    AddAttachments(openFileDialog.FileNames.ToList());
                }
            };

            PreviewItemsControl.Items.Add(button);
        }

        private void AddPreviewItem(AttachmentPreviewItem item)
        {
            Border previewBorder = new Border
            {
                Height = 156,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                Background = System.Windows.Media.Brushes.Black,
                Tag = item,
                ClipToBounds = true
            };

            if (item.FileType == UploadFileType.MessageAttachmentImage ||
                item.FileType == UploadFileType.MessageAttachmentGif)
            {
                // Show image preview
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(Path.GetFullPath(item.FilePath));
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    double width = 156;
                    if (bitmap.PixelHeight > 0)
                    {
                        double ratio = (double)bitmap.PixelWidth / bitmap.PixelHeight;
                        width = 156 * ratio;
                    }

                    // Max 2:1
                    if (width > 156 * 2) width = 156 * 2;

                    previewBorder.Width = width;

                    var image = new Image
                    {
                        Source = bitmap,
                        Stretch = System.Windows.Media.Stretch.UniformToFill
                    };
                    previewBorder.Child = image;
                }
                catch
                {
                    // Fallback to file icon if image can't be loaded
                    previewBorder.Width = 156;
                    ShowFileIcon(previewBorder, item);
                }
            }
            else
            {
                previewBorder.Width = 156;
                if (item.FileType == UploadFileType.MessageAttachmentVideo)
                {
                    // Show video icon with file name
                    ShowFileIcon(previewBorder, item, Wpf.Ui.Controls.SymbolRegular.Video24);
                }
                else
                {
                    // Show document icon with file name
                    ShowFileIcon(previewBorder, item, Wpf.Ui.Controls.SymbolRegular.Document24);
                }
            }

            PreviewItemsControl.Items.Add(previewBorder);
        }

        private void ShowFileIcon(Border previewBorder, AttachmentPreviewItem item, Wpf.Ui.Controls.SymbolRegular symbol = Wpf.Ui.Controls.SymbolRegular.Document24)
        {
            var stackPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = symbol,
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

        public void Clear()
        {
            // Clean up temp files from clipboard
            foreach (var item in _attachments.Where(a => a.IsFromClipboard))
            {
                try
                {
                    if (File.Exists(item.FilePath))
                        File.Delete(item.FilePath);
                }
                catch
                {
                    // Ignore errors deleting temp files - they will be cleaned up by OS eventually
                }
            }

            _attachments.Clear();
            _hasNonImageFiles = false;
            PreviewItemsControl.Items.Clear();
            MessageTextBox.Clear();
            SendAsFileCheckBox.IsChecked = false;
            SendAsFileCheckBox.IsEnabled = true;
            SendAsFileCheckBox.Visibility = Visibility.Collapsed;
            UpdateHeader();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            OnCancel?.Invoke(this, EventArgs.Empty);
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var args = new SendAttachmentsEventArgs
            {
                Attachments = _attachments,
                SendSeparately = false,
                MessageText = MessageTextBox.Text ?? string.Empty,
                SendAsFile = SendAsFileCheckBox.IsChecked == true
            };
            OnSend?.Invoke(this, args);
        }

        private void SendSeparatelyButton_Click(object sender, RoutedEventArgs e)
        {
            SendOptionsPopup.IsOpen = false;
            var args = new SendAttachmentsEventArgs
            {
                Attachments = _attachments,
                SendSeparately = true,
                MessageText = MessageTextBox.Text ?? string.Empty,
                SendAsFile = SendAsFileCheckBox.IsChecked == true
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
        public UploadFileType? OriginalFileType { get; set; }
        public bool IsFromClipboard { get; set; } = false;
    }

    public class SendAttachmentsEventArgs : EventArgs
    {
        public List<AttachmentPreviewItem> Attachments { get; set; } = new List<AttachmentPreviewItem>();
        public bool SendSeparately { get; set; }
        public string MessageText { get; set; } = string.Empty;
        public bool SendAsFile { get; set; }
    }
}
