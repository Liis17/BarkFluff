using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
    }
}
