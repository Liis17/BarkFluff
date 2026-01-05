using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

        /// <summary>
        /// URL аватара чата
        /// </summary>
        public string AvatarUrl => _url;

        /// <summary>
        /// Название чата
        /// </summary>
        public string ChatTitle => _title;

        /// <summary>
        /// ID последнего сообщения
        /// </summary>
        public long LastMessageId => _lastMessageId;

        /// <summary>
        /// Является ли чат групповым
        /// </summary>
        public bool IsGroupChat => _isGroupChat;

        /// <summary>
        /// ID пользователя (собеседника)
        /// </summary>
        public long UserId => _userId;

        /// <summary>
        /// ID файла аватара (уже извлечённый из URL)
        /// </summary>
        public string? AvatarFileId => _avatarFileId;

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

            // Устанавливаем данные для CachedAvatar
            AvatarControl.FileId = _avatarFileId;
            AvatarControl.FileUrl = imageUrl;

            UpdateUnreadBadge();
        }

        public void UpdateMessage()
        {
            // Update the last message ID so pagination works correctly
            _lastMessageId = TransferMessage.MessageId;
            LastMessage.Text = ProcessText(GetDisplayText(TransferMessage));
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

        /// <summary>
        /// Получает текст для отображения в ChatItem с учётом вложений
        /// </summary>
        public static string GetDisplayText(MessageModel message)
        {
            if (message == null)
                return string.Empty;

            // Если есть текст, возвращаем его
            if (!string.IsNullOrEmpty(message.Text))
                return message.Text;

            // Если текста нет, но есть вложения - показываем тип вложения
            if (message.Attachments != null && message.Attachments.Count > 0)
            {
                return FormatAttachmentText(message.Attachments[0].Type, message.Attachments.Count);
            }

            return string.Empty;
        }

        /// <summary>
        /// Получает текст для отображения из proto-сообщения с учётом вложений
        /// </summary>
        public static string GetDisplayTextFromProto(BarkFluff.Proto.Shared.Message? message)
        {
            if (message == null)
                return string.Empty;

            // Если есть текст, возвращаем его
            if (!string.IsNullOrEmpty(message.Content?.Text))
                return message.Content.Text;

            // Если текста нет, но есть вложения - показываем тип вложения
            if (message.Content?.Attachments != null && message.Content.Attachments.Count > 0)
            {
                return FormatAttachmentText(message.Content.Attachments[0].Type, message.Content.Attachments.Count);
            }

            return string.Empty;
        }

        /// <summary>
        /// Форматирует текст для отображения типа вложения
        /// </summary>
        private static string FormatAttachmentText(Proto.Shared.MessageAttachmentType type, int count)
        {
            return type switch
            {
                Proto.Shared.MessageAttachmentType.Image => count > 1 ? $"📷 Фото ({count})" : "📷 Фото",
                Proto.Shared.MessageAttachmentType.Video => count > 1 ? $"🎬 Видео ({count})" : "🎬 Видео",
                Proto.Shared.MessageAttachmentType.Gif => count > 1 ? $"🎞️ GIF ({count})" : "🎞️ GIF",
                Proto.Shared.MessageAttachmentType.Document => count > 1 ? $"📎 Файл ({count})" : "📎 Файл",
                _ => count > 1 ? $"📎 Вложение ({count})" : "📎 Вложение"
            };
        }

        /// <summary>
        /// Обновляет индикатор онлайн статуса пользователя
        /// </summary>
        public void UpdateOnlineStatus(BarkFluff.Proto.Onliner.UserOnlineStatus status)
        {
            // Показываем индикатор только для приватных чатов
            if (_isGroupChat)
            {
                Dispatcher.Invoke(() => OnlineIndicator.Visibility = Visibility.Collapsed);
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline)
                {
                    OnlineIndicator.Visibility = Visibility.Visible;
                }
                else
                {
                    OnlineIndicator.Visibility = Visibility.Collapsed;
                }
            });
        }
    }
}
