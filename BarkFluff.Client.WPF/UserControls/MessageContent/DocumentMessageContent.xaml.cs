using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

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
        public string? FileId { get; private set; }

        public DocumentMessageContent()
        {
            InitializeComponent();
            DocumentPanel.MouseLeftButtonDown += DocumentPanel_MouseLeftButtonDown;
        }

        public DocumentMessageContent(AttachmentsModel attachment) : this()
        {
            FileId = attachment.FileId;

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

        private void DocumentPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(FileId))
            {
                var msgType = new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Info
                };
                App.ErideMessage.AddMessage($"Document clicked: {FileId}", msgType);
            }
            e.Handled = true;
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
