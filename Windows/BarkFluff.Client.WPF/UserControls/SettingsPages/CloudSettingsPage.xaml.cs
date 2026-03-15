using BarkFluff.Proto.Files;

using System.Globalization;
using System.Windows;
using System.Windows.Media;

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

                // Разбивка по типам
                long imageSize = 0, videoSize = 0, documentSize = 0, audioSize = 0, voiceSize = 0;
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
                        case UploadFileType.MessageAttachmentAudio:
                            audioSize += st.Value;
                            break;
                        case UploadFileType.MessageAttachmentVoice:
                            voiceSize += st.Value;
                            break;
                    }
                }

                if (imageSize > 0) { ImagesSize.Text = FormatBytes(imageSize); ImagesBorder.Visibility = Visibility.Visible; }
                if (videoSize > 0) { VideosSize.Text = FormatBytes(videoSize); VideosBorder.Visibility = Visibility.Visible; }
                if (documentSize > 0) { DocumentsSize.Text = FormatBytes(documentSize); DocumentsBorder.Visibility = Visibility.Visible; }
                if (audioSize > 0) { AudioSize.Text = FormatBytes(audioSize); AudioBorder.Visibility = Visibility.Visible; }
                if (voiceSize > 0) { VoiceSize.Text = FormatBytes(voiceSize); VoiceBorder.Visibility = Visibility.Visible; }

                // Обновить сегментный прогресс-бар
                Dispatcher.Invoke(() =>
                {
                    StorageProgress.ClearSegments();
                    if (imageSize > 0) StorageProgress.AddSegment(Math.Max(1, (int)(imageSize / 1024)), new SolidColorBrush(Color.FromRgb(0x57, 0xBB, 0x62)));
                    if (videoSize > 0) StorageProgress.AddSegment(Math.Max(1, (int)(videoSize / 1024)), new SolidColorBrush(Color.FromRgb(0x3D, 0x50, 0xB7)));
                    if (documentSize > 0) StorageProgress.AddSegment(Math.Max(1, (int)(documentSize / 1024)), new SolidColorBrush(Color.FromRgb(0xCA, 0x6D, 0x34)));
                    if (audioSize > 0) StorageProgress.AddSegment(Math.Max(1, (int)(audioSize / 1024)), new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6)));
                    if (voiceSize > 0) StorageProgress.AddSegment(Math.Max(1, (int)(voiceSize / 1024)), new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)));
                    long freeSpace = totalBytes - usedBytes;
                    if (freeSpace > 0) StorageProgress.AddSegment(Math.Max(1, (int)(freeSpace / 1024)), StorageProgress.EmptyBrush);
                    StorageProgress.AnimStart();
                });
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
