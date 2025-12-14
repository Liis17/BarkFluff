using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
        private long _currentUserId;
        private List<long> _lastMessageReadBy = new List<long>();
        private long _lastMessageSenderId;
        private int _unreadCount;

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
            _currentUserId = App.GParam.UserId;
            _lastMessageReadBy = readBy ?? new List<long>();
            _lastMessageSenderId = 0; // Will be set later via TransferMessage
            _unreadCount = (int)unReaded;
            TimeMessage.Text = FormatDateTime(time.Length >= 2 ? time.Substring(1, time.Length - 2) : time);

            // Пытаемся извлечь fileId из URL если это не placeholder
            if (!string.IsNullOrEmpty(imageUrl) && !FileCacheService.IsPlaceholder(imageUrl))
            {
                _avatarFileId = FileCacheService.ExtractFileIdFromUrl(imageUrl);
            }

            Loaded += ChatItem_Loaded;
            UpdateUnreadBadge();
        }

        public void UpdateMessage()
        {
            // Update the last message ID so pagination works correctly
            _lastMessageId = TransferMessage.MessageId;
            LastMessage.Text = ProcessText(TransferMessage.Text);
            var time = TransferMessage.SentAt.ToString();
            TimeMessage.Text = FormatDateTime(time.Length >= 2 ? time.Substring(1, time.Length - 2) : time);
            
            // Update read status
            _lastMessageReadBy = TransferMessage.ReadBy ?? new List<long>();
            _lastMessageSenderId = TransferMessage.SenderId;
            UpdateReadStatusIndicator();
        }

        /// <summary>
        /// Updates the unread badge visibility and count
        /// </summary>
        public void UpdateUnreadBadge()
        {
            Dispatcher.Invoke(() =>
            {
                if (_unreadCount > 0)
                {
                    UnreadBadge.Visibility = Visibility.Visible;
                    UnreadCountText.Text = _unreadCount > 99 ? "99+" : _unreadCount.ToString();
                }
                else
                {
                    UnreadBadge.Visibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>
        /// Sets the unread count for this chat
        /// </summary>
        public void SetUnreadCount(int count)
        {
            _unreadCount = count;
            UpdateUnreadBadge();
        }

        /// <summary>
        /// Increments the unread count
        /// </summary>
        public void IncrementUnreadCount()
        {
            _unreadCount++;
            UpdateUnreadBadge();
        }

        /// <summary>
        /// Resets the unread count to zero
        /// </summary>
        public void ResetUnreadCount()
        {
            _unreadCount = 0;
            UpdateUnreadBadge();
        }

        /// <summary>
        /// Updates the read status for a specific message by ID (called when read receipt is received)
        /// </summary>
        public void UpdateLastMessageReadStatus(long messageId, List<long> readBy)
        {
            // Only update if this is the last message in the chat
            if (_lastMessageId == messageId)
            {
                _lastMessageReadBy = readBy;
                UpdateReadStatusIndicator();
            }
        }

        /// <summary>
        /// Updates the read status for the last message (called when read receipt is received)
        /// </summary>
        public void UpdateLastMessageReadStatus(List<long> readBy)
        {
            _lastMessageReadBy = readBy;
            UpdateReadStatusIndicator();
        }

        /// <summary>
        /// Updates the read status checkmarks based on the last message
        /// </summary>
        private void UpdateReadStatusIndicator()
        {
            Dispatcher.Invoke(() =>
            {
                // Only show read status for messages sent by current user
                if (_lastMessageSenderId != _currentUserId)
                {
                    ReadStatusPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                ReadStatusPanel.Visibility = Visibility.Visible;

                // Check if message has been read by others
                var readByOthers = _lastMessageReadBy.Any(id => id != _currentUserId);

                if (readByOthers)
                {
                    // Double checkmark - message read
                    FirstCheckmark.Visibility = Visibility.Visible;
                    SecondCheckmark.Visibility = Visibility.Visible;
                    ReadStatusPanel.Opacity = 1.0;
                }
                else
                {
                    // Single checkmark - message sent but not read
                    FirstCheckmark.Visibility = Visibility.Visible;
                    SecondCheckmark.Visibility = Visibility.Collapsed;
                    ReadStatusPanel.Opacity = 1.0;
                }
            });
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

                // For local/cached files, DownloadCompleted won't fire, so we need to handle both cases
                if (bitmapImage.IsDownloading)
                {
                    bitmapImage.DownloadCompleted += async (s, args) =>
                    {
                        await UpdateAvatarShadowEffect(imagePath);
                    };

                    bitmapImage.DownloadFailed += (s, args) =>
                    {
                        SetDefaultShadowEffect();
                    };
                }
                else
                {
                    // Image is already loaded (local/cached file)
                    await UpdateAvatarShadowEffect(imagePath);
                }
            }
            catch
            {
                SetDefaultShadowEffect();
            }
        }

        private async Task UpdateAvatarShadowEffect(string imagePath)
        {
            try
            {
                // Use cached image path for color analysis
                Color averageColor = await App.ColorAnalyzer.GetAverageColorFromUrlAsync(imagePath);
                Dispatcher.Invoke(() =>
                {
                    DropShadowEffect shadowEffect = new DropShadowEffect
                    {
                        BlurRadius = 12,
                        Opacity = 0.9,
                        ShadowDepth = 0,
                        Color = averageColor
                    };
                    border.Effect = shadowEffect;
                });
            }
            catch
            {
                Dispatcher.Invoke(() => SetDefaultShadowEffect());
            }
        }

        private void SetDefaultShadowEffect()
        {
            border.Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                Opacity = 0.3,
                ShadowDepth = 0,
                Color = Colors.Gray
            };
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
