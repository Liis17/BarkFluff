using BarkFluff.Proto.Files;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
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

                // Разбивка по всем типам из UploadFileType
                long imageSize = 0, videoSize = 0, gifSize = 0, documentSize = 0,
                     audioSize = 0, voiceSize = 0, stickerSize = 0, avatarSize = 0;

                foreach (var st in userSize.storageByType)
                {
                    switch (st.Key)
                    {
                        case UploadFileType.MessageAttachmentImage:
                            imageSize += st.Value;
                            break;
                        case UploadFileType.MessageAttachmentVideo:
                            videoSize += st.Value;
                            break;
                        case UploadFileType.MessageAttachmentGif:
                            gifSize += st.Value;
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
                        case UploadFileType.MessageAttachmentSticker:
                            stickerSize += st.Value;
                            break;
                        case UploadFileType.UserAvatar:
                        case UploadFileType.ChatPicture:
                            avatarSize += st.Value;
                            break;
                    }
                }

                bool anyType = imageSize > 0 || videoSize > 0 || gifSize > 0 || documentSize > 0
                            || audioSize > 0 || voiceSize > 0 || stickerSize > 0 || avatarSize > 0;

                if (anyType) TypesGrid.Visibility = Visibility.Visible;

                void ShowBlock(Border border, TextBlock label, long size)
                {
                    if (size > 0) { label.Text = FormatBytes(size); border.Visibility = Visibility.Visible; }
                }

                ShowBlock(ImagesBorder,   ImagesSize,   imageSize);
                ShowBlock(VideosBorder,   VideosSize,   videoSize);
                ShowBlock(GifBorder,      GifSize,      gifSize);
                ShowBlock(DocumentsBorder,DocumentsSize,documentSize);
                ShowBlock(AudioBorder,    AudioSize,    audioSize);
                ShowBlock(VoiceBorder,    VoiceSize,    voiceSize);
                ShowBlock(StickersBorder, StickersSize, stickerSize);
                ShowBlock(AvatarsBorder,  AvatarsSize,  avatarSize);

                // Прогресс-бар
                Dispatcher.Invoke(() =>
                {
                    StorageProgress.ClearSegments();
                    void AddSeg(long sz, Color c) { if (sz > 0) StorageProgress.AddSegment(Math.Max(1, (int)(sz / 1024)), new SolidColorBrush(c)); }
                    AddSeg(imageSize,    Color.FromRgb(0x57, 0xBB, 0x62));
                    AddSeg(videoSize,    Color.FromRgb(0x3D, 0x50, 0xB7));
                    AddSeg(gifSize,      Color.FromRgb(0xE8, 0xA8, 0x38));
                    AddSeg(documentSize, Color.FromRgb(0xCA, 0x6D, 0x34));
                    AddSeg(audioSize,    Color.FromRgb(0x9B, 0x59, 0xB6));
                    AddSeg(voiceSize,    Color.FromRgb(0x1A, 0xBC, 0x9C));
                    AddSeg(stickerSize,  Color.FromRgb(0xEC, 0x63, 0x96));
                    AddSeg(avatarSize,   Color.FromRgb(0x79, 0xAE, 0xDC));
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
