using BarkFluff.Proto.Files;

using Microsoft.Win32;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        private bool _isDragging = false;

        public AttachmentPreviewOverlay()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Устанавливает текст сообщения в MessageTextBox
        /// </summary>
        /// <param name="text">Текст для установки, или пустая строка если null</param>
        public void SetMessageText(string text)
        {
            MessageTextBox.Text = text ?? string.Empty;
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

                var fileInfo = new FileInfo(filePath);
                var item = new AttachmentPreviewItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileType = DetermineFileType(filePath),
                    FileSize = fileInfo.Exists ? fileInfo.Length : 0
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

            // Save clipboard image to temp file as JPEG
            var tempPath = Path.Combine(Path.GetTempPath(), $"clipboard_{Guid.NewGuid()}.jpg");
            using (var fileStream = new FileStream(tempPath, FileMode.Create))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fileStream);
            }

            var fileInfo = new FileInfo(tempPath);
            var item = new AttachmentPreviewItem
            {
                FilePath = tempPath,
                FileName = "Изображение из буфера обмена",
                FileType = UploadFileType.MessageAttachmentImage,
                IsFromClipboard = true,
                FileSize = fileInfo.Exists ? fileInfo.Length : 0
            };

            _attachments.Add(item);
            RefreshUI();
        }

        private void RefreshUI()
        {
            // Clear both containers
            ImagesGrid.Children.Clear();
            FilesList.Children.Clear();

            // Determine display mode
            bool allImages = _attachments.Count > 0 && _attachments.All(a => IsImageFileType(a.FileType));

            if (allImages)
            {
                // Show image grid (Telegram style)
                ImagesScrollViewer.Visibility = Visibility.Visible;
                FilesScrollViewer.Visibility = Visibility.Collapsed;

                foreach (var item in _attachments)
                {
                    AddImagePreviewItem(item);
                }
            }
            else
            {
                // Show file list
                ImagesScrollViewer.Visibility = Visibility.Collapsed;
                FilesScrollViewer.Visibility = Visibility.Visible;

                foreach (var item in _attachments)
                {
                    AddFilePreviewItem(item);
                }
            }

            UpdateHeader();
            UpdateSendAsFileCheckbox();
            UpdateAddButton();
        }

        /// <summary>
        /// Checks if the file type is an image type (image or gif)
        /// </summary>
        private static bool IsImageFileType(UploadFileType fileType)
        {
            return fileType == UploadFileType.MessageAttachmentImage ||
                   fileType == UploadFileType.MessageAttachmentGif;
        }

        /// <summary>
        /// Checks if the file type is a media type (not Document)
        /// </summary>
        private static bool IsMediaFileType(UploadFileType fileType)
        {
            return fileType != UploadFileType.MessageAttachmentDocument;
        }

        private void UpdateSendAsFileCheckbox()
        {
            bool hasMedia = false;

            foreach (var item in _attachments)
            {
                var typeToCheck = item.OriginalFileType ?? item.FileType;

                if (IsMediaFileType(typeToCheck))
                {
                    hasMedia = true;
                    break;
                }
            }

            if (hasMedia)
            {
                SendAsFileCheckBox.Visibility = Visibility.Visible;
                SendAsFileCheckBox.IsEnabled = true;
            }
            else
            {
                SendAsFileCheckBox.Visibility = Visibility.Collapsed;
            }
        }

        private void SendAsFileCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (SendAsFileCheckBox.IsChecked == true)
            {
                // Все не-Document файлы отправляем как Document
                foreach (var item in _attachments)
                {
                    if (item.FileType != UploadFileType.MessageAttachmentDocument)
                    {
                        item.OriginalFileType = item.FileType;
                        item.FileType = UploadFileType.MessageAttachmentDocument;
                    }
                }
            }
            else
            {
                // Восстанавливаем оригинальные типы
                foreach (var item in _attachments)
                {
                    if (item.OriginalFileType.HasValue)
                    {
                        item.FileType = item.OriginalFileType.Value;
                        item.OriginalFileType = null;
                    }
                }
            }

            RefreshUI();
        }

        private void UpdateHeader()
        {
            if (_attachments.Count == 0)
            {
                HeaderTextBlock.Text = "Предпросмотр вложений";
                return;
            }

            bool allImages = _attachments.All(a => IsImageFileType(a.FileType));

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

        private void UpdateAddButton()
        {
            // Disable add button if max attachments reached
            if (AddAttachmentButton != null)
            {
                AddAttachmentButton.IsEnabled = _attachments.Count < MaxAttachments;
                AddAttachmentButton.Opacity = _attachments.Count < MaxAttachments ? 1.0 : 0.5;
            }
        }


        private void AddImagePreviewItem(AttachmentPreviewItem item)
        {
            // Container grid for hover effects
            var containerGrid = new Grid
            {
                Margin = new Thickness(4),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Image preview border - Telegram style larger images
            Border previewBorder = new Border
            {
                Width = 160,
                Height = 160,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                ClipToBounds = true
            };

            try
            {
                if (!File.Exists(item.FilePath))
                {
                    ShowImagePlaceholder(previewBorder, item);
                }
                else
                {
                    byte[] imageBytes = File.ReadAllBytes(item.FilePath);

                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(imageBytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                    }

                    bitmap.Freeze();

                    var image = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.UniformToFill
                    };
                    previewBorder.Child = image;
                }
            }
            catch
            {
                ShowImagePlaceholder(previewBorder, item);
            }

            containerGrid.Children.Add(previewBorder);

            // Delete button overlay
            var deleteButton = new Wpf.Ui.Controls.Button
            {
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6),
                Background = new SolidColorBrush(Color.FromArgb(200, 40, 40, 40)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(0),
                Opacity = 0
            };

            var deleteIcon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss24,
                FontSize = 14,
                Foreground = Brushes.White
            };
            deleteButton.Content = deleteIcon;

            deleteButton.Click += (s, e) =>
            {
                _attachments.Remove(item);
                RefreshUI();
            };

            containerGrid.Children.Add(deleteButton);

            // Hover effects
            containerGrid.MouseEnter += (s, e) =>
            {
                deleteButton.Opacity = 1;
            };

            containerGrid.MouseLeave += (s, e) =>
            {
                deleteButton.Opacity = 0;
            };

            ImagesGrid.Children.Add(containerGrid);
        }

        private void ShowImagePlaceholder(Border previewBorder, AttachmentPreviewItem item)
        {
            var grid = new Grid();
            grid.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3D));

            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Image24,
                FontSize = 36,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))
            };

            grid.Children.Add(icon);
            previewBorder.Child = grid;
        }

        private void AddFilePreviewItem(AttachmentPreviewItem item)
        {
            // File list item container - Windows 11 style
            var itemBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(12),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = item
            };

            var itemGrid = new Grid();
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // File icon
            var iconBorder = new Border
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(8)
            };

            var iconColor = GetFileIconColor(item.FileType);
            iconBorder.Background = new SolidColorBrush(iconColor);

            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = GetFileIconSymbol(item.FileType),
                FontSize = 18,
                Foreground = Brushes.White
            };
            iconBorder.Child = icon;
            Grid.SetColumn(iconBorder, 0);
            itemGrid.Children.Add(iconBorder);

            // File info (name and size)
            var infoStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var fileNameText = new TextBlock
            {
                Text = item.FileName,
                FontFamily = (FontFamily)Application.Current.Resources["AdwaitaSans"],
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 320
            };
            infoStack.Children.Add(fileNameText);

            var fileSizeText = new TextBlock
            {
                Text = FormatFileSize(item.FileSize),
                FontFamily = (FontFamily)Application.Current.Resources["AdwaitaSans"],
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 2, 0, 0)
            };
            infoStack.Children.Add(fileSizeText);

            Grid.SetColumn(infoStack, 1);
            itemGrid.Children.Add(infoStack);

            // Delete button
            var deleteButton = new Wpf.Ui.Controls.Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Opacity = 0
            };

            var deleteIcon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss24,
                FontSize = 14
            };
            deleteButton.Content = deleteIcon;

            deleteButton.Click += (s, e) =>
            {
                _attachments.Remove(item);
                RefreshUI();
            };

            Grid.SetColumn(deleteButton, 2);
            itemGrid.Children.Add(deleteButton);

            itemBorder.Child = itemGrid;

            // Hover effects
            itemBorder.MouseEnter += (s, e) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                deleteButton.Opacity = 1;
            };

            itemBorder.MouseLeave += (s, e) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                deleteButton.Opacity = 0;
            };

            FilesList.Children.Add(itemBorder);
        }

        private Color GetFileIconColor(UploadFileType fileType)
        {
            return fileType switch
            {
                UploadFileType.MessageAttachmentVideo => Color.FromRgb(0x4C, 0xAF, 0x50),  // Green
                UploadFileType.MessageAttachmentAudio => Color.FromRgb(0xFF, 0x9E, 0x80),  // Orange
                UploadFileType.MessageAttachmentGif => Color.FromRgb(0xE9, 0x1E, 0x63),    // Pink
                _ => Color.FromRgb(0x21, 0x96, 0xF3)  // Blue for documents
            };
        }

        private Wpf.Ui.Controls.SymbolRegular GetFileIconSymbol(UploadFileType fileType)
        {
            return fileType switch
            {
                UploadFileType.MessageAttachmentVideo => Wpf.Ui.Controls.SymbolRegular.Video24,
                UploadFileType.MessageAttachmentAudio => Wpf.Ui.Controls.SymbolRegular.MusicNote124,
                UploadFileType.MessageAttachmentGif => Wpf.Ui.Controls.SymbolRegular.Gif24,
                _ => Wpf.Ui.Controls.SymbolRegular.Document24
            };
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "Б", "КБ", "МБ", "ГБ" };
            int i = 0;
            double size = bytes;

            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }

            return $"{size:0.##} {suffixes[i]}";
        }

        private UploadFileType DetermineFileType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
            var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm" };
            var gifExtensions = new[] { ".gif" };
            var audioExtensions = new[] { ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma" };

            if (imageExtensions.Contains(extension))
                return UploadFileType.MessageAttachmentImage;
            else if (videoExtensions.Contains(extension))
                return UploadFileType.MessageAttachmentVideo;
            else if (gifExtensions.Contains(extension))
                return UploadFileType.MessageAttachmentGif;
            else if (audioExtensions.Contains(extension))
                return UploadFileType.MessageAttachmentAudio;
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
            ImagesGrid.Children.Clear();
            FilesList.Children.Clear();
            MessageTextBox.Clear();
            SendAsFileCheckBox.IsChecked = false;
            SendAsFileCheckBox.IsEnabled = true;
            SendAsFileCheckBox.Visibility = Visibility.Collapsed;
            SendSeparatelyCheckBox.IsChecked = false;
            UpdateHeader();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            OnCancel?.Invoke(this, EventArgs.Empty);
        }

        private void AddAttachmentButton_Click(object sender, RoutedEventArgs e)
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
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var args = new SendAttachmentsEventArgs
            {
                Attachments = _attachments,
                SendSeparately = SendSeparatelyCheckBox.IsChecked == true,
                MessageText = MessageTextBox.Text ?? string.Empty,
                SendAsFile = SendAsFileCheckBox.IsChecked == true
            };
            OnSend?.Invoke(this, args);
        }

        #region Drag & Drop handlers

        private void UserControl_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && _attachments.Count < MaxAttachments)
                {
                    e.Effects = DragDropEffects.Copy;
                    _isDragging = true;
                    UpdateDragVisualFeedback(true);
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void UserControl_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && _attachments.Count < MaxAttachments)
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void UserControl_DragLeave(object sender, DragEventArgs e)
        {
            _isDragging = false;
            UpdateDragVisualFeedback(false);
            e.Handled = true;
        }

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            _isDragging = false;
            UpdateDragVisualFeedback(false);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    AddAttachments(files.ToList());
                }
            }
            e.Handled = true;
        }

        private void UpdateDragVisualFeedback(bool isDragging)
        {
            if (MainBorder != null)
            {
                if (isDragging)
                {
                    MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)); // Windows 11 accent blue
                    MainBorder.BorderThickness = new Thickness(2);
                }
                else
                {
                    MainBorder.BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorSecondary"];
                    MainBorder.BorderThickness = new Thickness(1);
                }
            }
        }

        #endregion

    }

    public class AttachmentPreviewItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public UploadFileType FileType { get; set; }
        public UploadFileType? OriginalFileType { get; set; }
        public bool IsFromClipboard { get; set; } = false;
        public long FileSize { get; set; } = 0;
    }

    public class SendAttachmentsEventArgs : EventArgs
    {
        public List<AttachmentPreviewItem> Attachments { get; set; } = new List<AttachmentPreviewItem>();
        public bool SendSeparately { get; set; }
        public string MessageText { get; set; } = string.Empty;
        public bool SendAsFile { get; set; }
    }
}
