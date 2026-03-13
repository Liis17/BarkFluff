using BarkFluff.Proto.Files;

using System.Globalization;
using System.Windows;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для CloudSettingsPage.xaml
    /// </summary>
    public partial class CloudSettingsPage : BaseSettingsPage
    {
        public override string Title => "Облако";

        public CloudSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            LoadStorageInfo();
        }

        private async void LoadStorageInfo()
        {
            try
            {
                var userSize = await App.ServerCommunication.GetUserStorageInfoAsync(App.GParam);
                long usedBytes = userSize.totalUsedSpace;
                long totalBytes = userSize.totalSpace;

                UsageText.Text = $"{FormatBytes(usedBytes)} из {FormatBytes(totalBytes)}";

                double percent = totalBytes > 0 ? (double)usedBytes / totalBytes * 100 : 0;
                UsagePercentText.Text = $"{percent:F1}% использовано";

                // Обновить прогресс-бар
                Dispatcher.Invoke(() =>
                {
                    var parent = UsageBar.Parent as FrameworkElement;
                    if (parent != null)
                    {
                        double barWidth = parent.ActualWidth > 0
                            ? parent.ActualWidth * (percent / 100)
                            : 0;
                        UsageBar.Width = Math.Max(0, barWidth);
                    }
                });

                // Разбивка по типам
                long imageSize = 0, videoSize = 0, documentSize = 0;
                foreach (var st in userSize.storageByType)
                {
                    switch (st.Key)
                    {
                        case UploadFileType.UserAvatar:
                        case UploadFileType.MessageAttachmentImage:
                        case UploadFileType.ChatPicture:
                            imageSize += st.Value;
                            break;
                        case UploadFileType.MessageAttachmentVideo:
                        case UploadFileType.MessageAttachmentGif:
                            videoSize += st.Value;
                            break;
                        case UploadFileType.MessageAttachmentDocument:
                            documentSize += st.Value;
                            break;
                    }
                }

                if (imageSize > 0) { ImagesSize.Text = FormatBytes(imageSize); ImagesBorder.Visibility = Visibility.Visible; }
                if (videoSize > 0) { VideosSize.Text = FormatBytes(videoSize); VideosBorder.Visibility = Visibility.Visible; }
                if (documentSize > 0) { DocumentsSize.Text = FormatBytes(documentSize); DocumentsBorder.Visibility = Visibility.Visible; }
            }
            catch
            {
                UsageText.Text = "Не удалось загрузить данные";
            }
        }

        private static string FormatBytes(long bytes)
        {
            const double OneMb = 1024.0 * 1024.0;
            const double OneGb = 1024.0 * 1024.0 * 1024.0;

            if (bytes >= OneGb)
                return (bytes / OneGb).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            return (bytes / OneMb).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
