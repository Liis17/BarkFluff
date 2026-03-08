using BarkFluff.Proto.Files;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для UsedSpaceProgressBar.xaml
    /// </summary>
    public partial class UsedSpaceProgressBar : UserControl
    {
        public UsedSpaceProgressBar()
        {
            InitializeComponent();
            Loaded += USPBLoaded;
        }

        private async void USPBLoaded(object sender, RoutedEventArgs e)
        {
            var userSize = await App.ServerCommunication.GetUserStorageInfoAsync(App.GParam);
            long usedBytes = userSize.totalUsedSpace;
            long totalBytes = userSize.totalSpace;
            UsedSpaceText.Text = $"{FormatBytes(usedBytes)}";
            LimitSpaceText.Text = $"{FormatBytes(totalBytes)}";

            UsedSpaceProgress.Value = usedBytes;
            UsedSpaceProgress.MaxValue = totalBytes;

            UsedSpaceProgress.AnimStart();

            // Получаем значения для каждого типа файлов
            long unknownSize = 0;
            long imageSize = 0;
            long videoSize = 0;
            long documentSize = 0;

            foreach (var storageType in userSize.storageByType)
            {
                switch (storageType.Key)
                {
                    case UploadFileType.UserAvatar:
                    case UploadFileType.MessageAttachmentImage:
                    case UploadFileType.ChatPicture:
                        imageSize += storageType.Value;
                        break;

                    case UploadFileType.MessageAttachmentVideo:
                    case UploadFileType.MessageAttachmentGif:
                        videoSize += storageType.Value;
                        break;

                    case UploadFileType.MessageAttachmentDocument:
                        documentSize += storageType.Value;
                        break;

                    default:
                        // Все остальные неизвестные типы
                        unknownSize += storageType.Value;
                        break;
                }
            }

            // Добавляем сегменты с соответствующими цветами, если размер больше 0
            // и обновляем текст с размером, скрываем если размер 0
            if (unknownSize > 0)
            {
                UnknownColor.Background = UsedTypeSpace.AddSegment((int)unknownSize, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE20101")));
                UnknownText.Text = $"Неизвестно ({FormatBytes(unknownSize)})";
                UnknownBorder.Visibility = Visibility.Visible;
            }
            else
            {
                UnknownBorder.Visibility = Visibility.Collapsed;
            }

            if (imageSize > 0)
            {
                ImageColor.Background = UsedTypeSpace.AddSegment((int)imageSize, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF57BB62")));
                ImageText.Text = $"Картинки ({FormatBytes(imageSize)})";
                ImageBorder.Visibility = Visibility.Visible;
            }
            else
            {
                ImageBorder.Visibility = Visibility.Collapsed;
            }

            if (videoSize > 0)
            {
                VideoColor.Background = UsedTypeSpace.AddSegment((int)videoSize, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3D50B7")));
                VideoText.Text = $"Видео ({FormatBytes(videoSize)})";
                VideoBorder.Visibility = Visibility.Visible;
            }
            else
            {
                VideoBorder.Visibility = Visibility.Collapsed;
            }

            if (documentSize > 0)
            {
                DocumentColor.Background = UsedTypeSpace.AddSegment((int)documentSize, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA6D34")));
                DocumentText.Text = $"Документы ({FormatBytes(documentSize)})";
                DocumentBorder.Visibility = Visibility.Visible;
            }
            else
            {
                DocumentBorder.Visibility = Visibility.Collapsed;
            }

            // Голосовые сообщения пока не реализованы, поэтому Border остается скрытым
            // VoiceBorder.Visibility уже установлен в Collapsed в XAML

            UsedTypeSpace.AnimStart();
        }

        private string FormatBytes(long bytes)
        {
            const double OneMb = 1024.0 * 1024.0;
            const double OneGb = 1024.0 * 1024.0 * 1024.0;

            // Если байтов больше или равно 1 ГБ (1024^3)
            if (bytes >= OneGb)
            {
                double result = bytes / OneGb;
                // CultureInfo.InvariantCulture гарантирует точку вместо запятой
                return result.ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            }
            else
            {
                // Если меньше 1 ГБ, переводим в МБ (даже если число очень маленькое)
                double result = bytes / OneMb;
                return result.ToString("0.##", CultureInfo.InvariantCulture) + " MB";
            }
        }
    }
}
