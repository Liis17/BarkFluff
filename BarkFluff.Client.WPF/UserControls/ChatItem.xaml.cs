using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using Color = System.Windows.Media.Color;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для ChatItem.xaml
    /// </summary>
    public partial class ChatItem : UserControl
    {
        /// <summary>
        /// Статус прочтения сообщения
        /// </summary>
        public enum ReadingStatus
        {
            /// <summary>
            /// Сообщение отправлено и прочитано мной
            /// </summary>
            My,

            /// <summary>
            /// Сообщение отправлено, но не прочитано собеседником
            /// </summary>
            OnlySent,

            /// <summary>
            /// Сообщение отправлено и прочитано собеседником
            /// </summary>
            SentAndRead,

            /// <summary>
            /// Сообщение отправлено мне, но не прочитано мной
            /// </summary>
            ForMe
        }
        public MessageModel TransferMessage { get; set; } //объект класса MessageModel для обновления этого блока в списке чатов (после считывания делать пустым)
        private string _url;
        private string? _avatarFileId;
        public string ChatId = "";
        private long _lastMessageId;
        private bool _isGroupChat;
        private long _userId;
        private string _title;
        public ChatItem(string imageUrl, string chatName, string lastMessageText, string time, ReadingStatus reading, List<long> readBy, long unReaded, string chatId, long lastMessageId, bool isGroupChat, long userId)
        {
            InitializeComponent();
            ChatId = chatId;
            _lastMessageId = lastMessageId;
            _isGroupChat = isGroupChat;
            Title.Text = chatName;
            _title = chatName;
            LastMessage.Text = ProcessText(lastMessageText);
            _url = imageUrl;
            _userId = userId;
            TimeMessage.Text = FormatDateTime(time.Length >= 2 ? time.Substring(1, time.Length - 2) : time);

            // Пытаемся извлечь fileId из URL если это не placeholder
            if (!string.IsNullOrEmpty(imageUrl) && !FileCacheService.IsPlaceholder(imageUrl))
            {
                _avatarFileId = FileCacheService.ExtractFileIdFromUrl(imageUrl);
            }

            Loaded += ChatItem_Loaded;
        }

        public void UpdateMessage()
        {
            //_lastMessageId = TransferMessage.MessageId;
            LastMessage.Text = ProcessText(TransferMessage.Text);
            var time = TransferMessage.SentAt.ToString();
            TimeMessage.Text = FormatDateTime(time.Length >= 2 ? time.Substring(1, time.Length - 2) : time);
        }

        private async void ChatItem_Loaded(object sender, RoutedEventArgs e)
        {
            // Используем кеш-сервис для загрузки аватара
            var imagePath = App.FileCacheService.GetCachedFilePath(_avatarFileId ?? string.Empty, FileType.Avatar, _url);
            SetAvatarImage(imagePath);

            // Подписываемся на событие кеширования файла
            App.FileCacheService.FileCached += OnFileCached;

            // Отписываемся при выгрузке контрола
            Unloaded += (s, args) =>
            {
                App.FileCacheService.FileCached -= OnFileCached;
            };
        }

        private void OnFileCached(string fileId, string filePath, FileType fileType)
        {
            if (fileId == _avatarFileId && fileType == FileType.Avatar)
            {
                Dispatcher.Invoke(() => SetAvatarImage(filePath));
            }
        }

        private async void SetAvatarImage(string imagePath)
        {
            try
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(imagePath, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();

                ImageBrush imageBrush = new ImageBrush
                {
                    ImageSource = bitmapImage,
                    Stretch = Stretch.UniformToFill,
                };
                border.Background = imageBrush;

                bitmapImage.DownloadCompleted += async (s, args) =>
                {
                    // Use the original URL for color analysis if available, otherwise use the cached path
                    var colorSourceUrl = !string.IsNullOrEmpty(_url) ? _url : imagePath;
                    Color averageColor = await App.ColorAnalyzer.GetAverageColorFromUrlAsync(colorSourceUrl);
                    DropShadowEffect shadowEffect = new DropShadowEffect
                    {
                        BlurRadius = 12,
                        Opacity = 0.9,
                        ShadowDepth = 0,
                        Color = averageColor
                    };
                    border.Effect = shadowEffect;
                };

                bitmapImage.DownloadFailed += (s, args) =>
                {
                    border.Effect = new DropShadowEffect
                    {
                        BlurRadius = 10,
                        Opacity = 0.3,
                        ShadowDepth = 0,
                        Color = Colors.Gray
                    };
                };
            }
            catch
            {
                border.Effect = new DropShadowEffect
                {
                    BlurRadius = 10,
                    Opacity = 0.3,
                    ShadowDepth = 0,
                    Color = Colors.Gray
                };
            }
        }

        private string FormatDateTime(string input)
        {
            if (!DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime dateTimeUtc))

            {
                return "Неверный формат даты";
            }

            DateTime localDateTime = dateTimeUtc.ToLocalTime();
            DateTime now = DateTime.Now;

            CultureInfo ruCulture = new CultureInfo("ru-RU");

            if (localDateTime.Date == now.Date)
            {
                return localDateTime.ToString("HH:mm");
            }

            System.Globalization.Calendar calendar = ruCulture.Calendar;
            CalendarWeekRule rule = ruCulture.DateTimeFormat.CalendarWeekRule;
            DayOfWeek firstDayOfWeek = ruCulture.DateTimeFormat.FirstDayOfWeek;

            int weekNow = calendar.GetWeekOfYear(now, rule, firstDayOfWeek);
            int weekThen = calendar.GetWeekOfYear(localDateTime, rule, firstDayOfWeek);

            if (localDateTime.Year == now.Year && weekThen == weekNow)
            {
                return localDateTime.ToString("ddd", ruCulture);
            }
            else if (localDateTime.Year == now.Year)
            {

                return localDateTime.ToString("dd MMM", ruCulture);
            }
            else
            {
                return localDateTime.ToString("dd MMM yyyy", ruCulture);
            }
        }

        private void UserControl_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            App.Messenger.OpenChatById(ChatId, _lastMessageId, _isGroupChat, _userId, _title);
        }

        private string ProcessText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            string result = input.Replace("\r\n", " ").Replace("\n", " ").Trim();
            return result.Length > 50 ? result.Substring(0, 50) : result;
        }
    }
}
