using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Status of file upload
    /// </summary>
    public enum UploadStatus
    {
        Pending,     // Waiting for upload
        Uploading,   // Upload in progress (show progress)
        Uploaded,    // Upload complete (show checkmark)
        Failed       // Upload failed (show error icon + retry button)
    }

    /// <summary>
    /// Control for displaying an attachment that is being uploaded
    /// </summary>
    public partial class UploadingAttachmentItem : UserControl
    {
        public string LocalFilePath { get; private set; } = string.Empty;
        public string? FileId { get; private set; }
        public UploadStatus Status { get; private set; } = UploadStatus.Pending;
        public string? ErrorMessage { get; private set; }

        private bool _isImage;

        public UploadingAttachmentItem()
        {
            InitializeComponent();
        }

        public UploadingAttachmentItem(string filePath) : this()
        {
            LocalFilePath = filePath;
            Initialize();
        }

        private void Initialize()
        {
            // Set file name
            FileNameText.Text = Path.GetFileName(LocalFilePath);

            // Set file size
            try
            {
                var fileInfo = new FileInfo(LocalFilePath);
                FileSizeText.Text = FormatFileSize(fileInfo.Length);
            }
            catch
            {
                FileSizeText.Text = "Unknown size";
            }

            // Check if file is an image
            var extension = Path.GetExtension(LocalFilePath).ToLowerInvariant();
            _isImage = extension == ".jpg" || extension == ".jpeg" || extension == ".png" || 
                       extension == ".gif" || extension == ".bmp" || extension == ".webp";

            // Load preview
            if (_isImage)
            {
                LoadImagePreview();
            }
            else
            {
                // Show document icon for non-images
                DocumentIcon.Visibility = Visibility.Visible;
                PreviewImage.Visibility = Visibility.Collapsed;
            }

            // Set initial status to Pending
            UpdateStatusUI();
        }

        private void LoadImagePreview()
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(LocalFilePath, UriKind.Absolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.DecodePixelWidth = 60;
                bitmapImage.EndInit();
                if (bitmapImage.CanFreeze)
                {
                    bitmapImage.Freeze();
                }

                PreviewImage.Source = bitmapImage;
                PreviewImage.Visibility = Visibility.Visible;
                DocumentIcon.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // If image load fails, show document icon instead
                DocumentIcon.Visibility = Visibility.Visible;
                PreviewImage.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Updates the upload progress (0.0 to 1.0)
        /// </summary>
        public void UpdateProgress(double progress)
        {
            if (Status != UploadStatus.Uploading)
            {
                Status = UploadStatus.Uploading;
            }

            Dispatcher.Invoke(() =>
            {
                var percentage = Math.Max(0, Math.Min(100, progress * 100));
                UploadProgressBar.Value = percentage;
                ProgressText.Text = $"{(int)percentage}%";
                UpdateStatusUI();
            });
        }

        /// <summary>
        /// Marks the attachment as successfully uploaded
        /// </summary>
        public void MarkAsUploaded(string fileId)
        {
            FileId = fileId;
            Status = UploadStatus.Uploaded;
            Dispatcher.Invoke(() => UpdateStatusUI());
        }

        /// <summary>
        /// Marks the attachment as failed
        /// </summary>
        public void MarkAsFailed(string error)
        {
            ErrorMessage = error;
            Status = UploadStatus.Failed;
            Dispatcher.Invoke(() => UpdateStatusUI());
        }

        private void UpdateStatusUI()
        {
            // Hide all status indicators first
            PendingIcon.Visibility = Visibility.Collapsed;
            UploadingPanel.Visibility = Visibility.Collapsed;
            UploadedIcon.Visibility = Visibility.Collapsed;
            FailedIcon.Visibility = Visibility.Collapsed;

            // Show the appropriate indicator
            switch (Status)
            {
                case UploadStatus.Pending:
                    PendingIcon.Visibility = Visibility.Visible;
                    break;

                case UploadStatus.Uploading:
                    UploadingPanel.Visibility = Visibility.Visible;
                    break;

                case UploadStatus.Uploaded:
                    UploadedIcon.Visibility = Visibility.Visible;
                    break;

                case UploadStatus.Failed:
                    FailedIcon.Visibility = Visibility.Visible;
                    if (!string.IsNullOrEmpty(ErrorMessage))
                    {
                        FailedIcon.ToolTip = $"Ошибка: {ErrorMessage}";
                    }
                    break;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.#} {sizes[order]}";
        }
    }
}
