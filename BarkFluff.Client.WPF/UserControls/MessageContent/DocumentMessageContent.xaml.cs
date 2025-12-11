using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class DocumentMessageContent : UserControl
    {
        public string? FileId { get; private set; }

        public DocumentMessageContent()
        {
            InitializeComponent();
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
