using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Wpf.Ui.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class DocumentMessageContent : UserControl
    {
        private static readonly HashSet<string> _safeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif",
            // Video
            ".mp4", ".avi", ".mov", ".mkv", ".webm",
            // Archives
            ".zip", ".rar", ".7z", ".tar", ".gz",
            // Text/Code
            ".txt", ".md", ".json", ".xml", ".csv", ".log", ".html", ".css", ".js", ".ts",
            ".cs", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".sql",
            ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
            // Documents
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            // Audio
            ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac",
        };

        private static readonly HashSet<string> _blockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".sh", ".msi", ".com", ".vbs", ".wsf", ".scr", ".pif", ".dll",
        };

        public string? FileId { get; private set; }
        private string? _fileName;
        private bool _isDownloading;

        public DocumentMessageContent()
        {
            InitializeComponent();
            DocumentPanel.MouseLeftButtonDown += DocumentPanel_MouseLeftButtonDown;
        }

        public DocumentMessageContent(AttachmentsModel attachment) : this()
        {
            FileId = attachment.FileId;
            _fileName = attachment.FileName;

            SetIconForType(attachment.Type);

            // Имя файла: приоритет FileName из сервера, затем fallback
            if (!string.IsNullOrEmpty(attachment.FileName))
            {
                DocumentFileName.Text = attachment.FileName;
            }
            else if (!string.IsNullOrEmpty(attachment.PreviewUrl))
            {
                DocumentFileName.Text = Path.GetFileName(attachment.PreviewUrl);
            }
            else
            {
                DocumentFileName.Text = GenerateFileName(attachment.FileId, attachment.Type);
            }

            // Размер файла
            if (attachment.Size > 0)
            {
                DocumentFileSize.Text = FormatFileSize(attachment.Size);
                DocumentFileSize.Visibility = Visibility.Visible;
            }
            else
            {
                DocumentFileSize.Visibility = Visibility.Collapsed;
            }

            // Превью-картинка если есть PreviewFileId
            if (!string.IsNullOrEmpty(attachment.PreviewFileId))
            {
                IconBorder.Visibility = Visibility.Collapsed;
                PreviewBorder.Visibility = Visibility.Visible;
                PreviewCachedImage.FileId = attachment.PreviewFileId;
                PreviewCachedImage.FileUrl = attachment.PreviewUrl;
                PreviewCachedImage.FileType = Services.App.Caching.FileType.Image;
            }
        }

        public DocumentMessageContent(string fileId, string previewUrl, long size) : this()
        {
            FileId = fileId;

            DocumentFileName.Text = !string.IsNullOrEmpty(previewUrl)
                ? Path.GetFileName(previewUrl)
                : $"Document_{fileId}";

            if (size > 0)
            {
                DocumentFileSize.Text = FormatFileSize(size);
                DocumentFileSize.Visibility = Visibility.Visible;
            }
            else
            {
                DocumentFileSize.Visibility = Visibility.Collapsed;
            }
        }

        public DocumentMessageContent(string fileId, string previewUrl, long size,
            BarkFluff.Proto.Shared.MessageAttachmentType type) : this()
        {
            FileId = fileId;

            // Установить иконку в зависимости от типа
            SetIconForType(type);

            // Установить имя файла
            DocumentFileName.Text = !string.IsNullOrEmpty(previewUrl)
                ? Path.GetFileName(previewUrl)
                : GenerateFileName(fileId, type);

            // Установить размер файла
            if (size > 0)
            {
                DocumentFileSize.Text = FormatFileSize(size);
                DocumentFileSize.Visibility = Visibility.Visible;
            }
            else
            {
                DocumentFileSize.Visibility = Visibility.Collapsed;
            }
        }

        private async void DocumentPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (string.IsNullOrEmpty(FileId) || _isDownloading) return;

            // Check extension safety
            var extension = Path.GetExtension(_fileName ?? string.Empty);
            if (_blockedExtensions.Contains(extension))
            {
                var msgType = new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Warning
                };
                App.ErideMessage.AddMessage($"Открытие файлов типа {extension} заблокировано в целях безопасности", msgType);
                return;
            }

            if (!string.IsNullOrEmpty(extension) && !_safeExtensions.Contains(extension))
            {
                var msgType = new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Warning
                };
                App.ErideMessage.AddMessage($"Неизвестный тип файла {extension}. Открытие заблокировано", msgType);
                return;
            }

            // Check if already cached
            if (App.FileCacheService.IsFileCached(FileId))
            {
                var cachedPath = App.FileCacheService.GetCachedFilePath(FileId, FileType.Document);
                OpenFile(cachedPath);
                return;
            }

            // Download with progress
            _isDownloading = true;
            DownloadProgressBar.Visibility = Visibility.Visible;

            var progress = new Progress<double>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadProgressBar.Value = (int)(p * 100);
                });
            });

            var result = await App.FileCacheService.DownloadAndCacheFileWithProgressAsync(
                FileId, FileType.Document, null, progress, _fileName);

            _isDownloading = false;
            DownloadProgressBar.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrEmpty(result))
            {
                OpenFile(result);
            }
        }

        private void OpenFile(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(filePath),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                var msgType = new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Error
                };
                App.ErideMessage.AddMessage($"Не удалось открыть файл: {ex.Message}", msgType);
            }
        }

        public void SetFileName(string fileName)
        {
            DocumentFileName.Text = fileName;
        }

        public void SetFileSize(long bytes)
        {
            if (bytes > 0)
            {
                DocumentFileSize.Text = FormatFileSize(bytes);
                DocumentFileSize.Visibility = Visibility.Visible;
            }
            else
            {
                DocumentFileSize.Visibility = Visibility.Collapsed;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void SetIconForType(BarkFluff.Proto.Shared.MessageAttachmentType type)
        {
            switch (type)
            {
                case BarkFluff.Proto.Shared.MessageAttachmentType.Image:
                case BarkFluff.Proto.Shared.MessageAttachmentType.Gif:
                    DocumentIcon.Symbol = SymbolRegular.Image16;
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Зеленый
                    break;

                case BarkFluff.Proto.Shared.MessageAttachmentType.Video:
                    DocumentIcon.Symbol = SymbolRegular.Video16;
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Красный
                    break;

                case BarkFluff.Proto.Shared.MessageAttachmentType.Audio:
                    DocumentIcon.Symbol = SymbolRegular.MusicNote120;
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)); // Фиолетовый
                    break;

                case BarkFluff.Proto.Shared.MessageAttachmentType.Document:
                default:
                    DocumentIcon.Symbol = SymbolRegular.Document16;
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x9F, 0xD4)); // Синий (текущий)
                    break;
            }
        }

        private string GenerateFileName(string fileId, BarkFluff.Proto.Shared.MessageAttachmentType type)
        {
            return type switch
            {
                BarkFluff.Proto.Shared.MessageAttachmentType.Image => $"Image_{fileId}",
                BarkFluff.Proto.Shared.MessageAttachmentType.Gif => $"Animation_{fileId}",
                BarkFluff.Proto.Shared.MessageAttachmentType.Video => $"Video_{fileId}",
                _ => $"Document_{fileId}"
            };
        }
    }
}
