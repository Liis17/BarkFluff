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

            // Save clipboard image to temp file as JPEG
            var tempPath = Path.Combine(Path.GetTempPath(), $"clipboard_{Guid.NewGuid()}.jpg");
            using (var fileStream = new FileStream(tempPath, FileMode.Create))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
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

        private void UpdateSendAsFileCheckbox()
        {
            // Проверяем есть ли картинки и не-картинки, используя оригинальный тип или расширение файла
            bool hasImages = false;
            bool hasNonImages = false;

            foreach (var item in _attachments)
            {
                // Используем оригинальный тип если он есть, иначе текущий
                var typeToCheck = item.OriginalFileType ?? item.FileType;
                
                if (IsImageFileType(typeToCheck))
                {
                    hasImages = true;
                }
                else
                {
                    hasNonImages = true;
                }
            }

            _hasNonImageFiles = hasNonImages;

            if (hasImages)
            {
                SendAsFileCheckBox.Visibility = Visibility.Visible;
                
                if (hasNonImages)
                {
                    // Force send as files when mixed content
                    SendAsFileCheckBox.IsChecked = true;
                    SendAsFileCheckBox.IsEnabled = false;
                }
                else
                {
                    // Only images - allow user to choose
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
                    if (IsImageFileType(item.FileType))
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
            
            // Обновляем весь UI чтобы превью переключилось между картинками и иконками
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


        private void AddPreviewItem(AttachmentPreviewItem item)
        {
            // Gradient background matching IncomingMessageStyle
            var gradientBrush = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
                GradientStops = new System.Windows.Media.GradientStopCollection
                {
                    new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3D), 0.0),
                    new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30), 1.0)
                }
            };

            Border previewBorder = new Border
            {
                Width = 180,
                Height = 180,
                Margin = new Thickness(6),
                CornerRadius = new CornerRadius(18, 18, 18, 6),
                Background = gradientBrush,
                Tag = item,
                ClipToBounds = true
            };

            // Drop shadow effect matching MessageBubble
            previewBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                Direction = 270,
                Opacity = 0.12,
                ShadowDepth = 3,
                Color = System.Windows.Media.Colors.Black
            };

            if (IsImageFileType(item.FileType))
            {
                // Show image preview
                try
                {
                    if (!File.Exists(item.FilePath))
                    {
                        ShowFileIcon(previewBorder, item);
                    }
                    else
                    {
                        // Загружаем файл полностью в byte array
                        byte[] imageBytes = File.ReadAllBytes(item.FilePath);
                        
                        var bitmap = new BitmapImage();
                        using (var stream = new MemoryStream(imageBytes))
                        {
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = stream;
                            bitmap.EndInit();
                        }
                        
                        // Замораживаем bitmap для thread-safety и производительности
                        bitmap.Freeze();

                        var image = new Image
                        {
                            Source = bitmap,
                            Stretch = System.Windows.Media.Stretch.UniformToFill
                        };
                        previewBorder.Child = image;
                    }
                }
                catch
                {
                    // Fallback to file icon if image can't be loaded
                    ShowFileIcon(previewBorder, item);
                }
            }
            else
            {
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

            // Wrap in Grid to add delete button
            var grid = new Grid();
            grid.Children.Add(previewBorder);

            // Add delete button overlay
            var deleteButton = new Wpf.Ui.Controls.Button
            {
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(220, 40, 40, 40)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(0),
                Opacity = 0,
                Tag = "DeleteButton"
            };

            var deleteIcon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss24,
                FontSize = 18,
                Foreground = System.Windows.Media.Brushes.White
            };
            deleteButton.Content = deleteIcon;

            deleteButton.Click += (s, e) =>
            {
                _attachments.Remove(item);
                RefreshUI();
            };

            // Add hover effect to show/hide delete button
            grid.MouseEnter += (s, e) =>
            {
                foreach (var child in grid.Children)
                {
                    if (child is Wpf.Ui.Controls.Button btn && btn.Tag?.ToString() == "DeleteButton")
                    {
                        btn.Opacity = 1;
                    }
                }
            };

            grid.MouseLeave += (s, e) =>
            {
                foreach (var child in grid.Children)
                {
                    if (child is Wpf.Ui.Controls.Button btn && btn.Tag?.ToString() == "DeleteButton")
                    {
                        btn.Opacity = 0;
                    }
                }
            };

            grid.Children.Add(deleteButton);

            PreviewItemsControl.Items.Add(grid);
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
                    MainBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(206, 100, 50)); // Orange accent
                    MainBorder.BorderThickness = new Thickness(2);
                }
                else
                {
                    MainBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(48, 255, 255, 255));
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
    }

    public class SendAttachmentsEventArgs : EventArgs
    {
        public List<AttachmentPreviewItem> Attachments { get; set; } = new List<AttachmentPreviewItem>();
        public bool SendSeparately { get; set; }
        public string MessageText { get; set; } = string.Empty;
        public bool SendAsFile { get; set; }
    }
}
