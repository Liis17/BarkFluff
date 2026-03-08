using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Globalization;
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
        private bool _isOnline;
        private DateTime? _lastSeen;
        private bool _isDraggingFiles = false;
        private long _firstUnreadId;
        private bool _isSelfChat;

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

        /// <summary>
        /// ID первого непрочитанного сообщения
        /// </summary>
        public long FirstUnreadId => _firstUnreadId;

        public ChatItem(string imageUrl, string chatName, string lastMessageText, string time, ReadingStatus reading, List<long> readBy, long unReaded, string chatId, long lastMessageId, long firstUnreadId, bool isGroupChat, long userId)
        {
            InitializeComponent();

            DragDropVisualOverlay.Visibility = Visibility.Collapsed; // Скрываем overlay по умолчанию
            BaseChatGrid.Visibility = Visibility.Visible;

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
            OnlineIndicator.Visibility = Visibility.Collapsed; // Скрываем онлайн-статус по умолчанию

            // Пытаемся извлечь fileId из URL если это не placeholder
            if (!string.IsNullOrEmpty(imageUrl) && !FileCacheService.IsPlaceholder(imageUrl))
            {
                _avatarFileId = FileCacheService.ExtractFileIdFromUrl(imageUrl);
            }

            // Устанавливаем данные для CachedAvatar

            if (imageUrl != "UserWithoutAvatar" && imageUrl != "SavedChat" && !string.IsNullOrEmpty(imageUrl))
            {
                //CachedAvatar cachedAvatar = new CachedAvatar();
                AvatarControl.FileId = _avatarFileId;
                AvatarControl.FileUrl = imageUrl;
                AvatarControl.AvatarType = AvatarType.Image;
            }
            else if (imageUrl == "SavedChat")
            {
                AvatarControl.FileId = null;
                AvatarControl.FileUrl = null;
                AvatarControl.AvatarType = AvatarType.SavedChat;

            }
            else if (imageUrl == "UserWithoutAvatar")
            {
                AvatarControl.FileId = null;
                AvatarControl.FileUrl = null;
                AvatarControl.AvatarType = AvatarType.UserWithoutAvatar;
            }
            else
            {
                AvatarControl.FileId = null;
                AvatarControl.FileUrl = null;
                AvatarControl.AvatarType = AvatarType.UserWithoutAvatar;
            }



            // Сохраняем ID первого непрочитанного сообщения
            _firstUnreadId = firstUnreadId;

            // Определяем, является ли чат чатом с собой (Избранное)
            _isSelfChat = (_userId == _currentUserId);

            // Применяем специальный стиль для чата с собой
            if (_isSelfChat)
            {
                ConfigureSelfChatAppearance();
            }

            UpdateUnreadBadge();

            // Subscribe to online status events
            this.Loaded += ChatItem_Loaded;
            this.Unloaded += ChatItem_Unloaded;
        }

        /// <summary>
        /// Настраивает специальный внешний вид для чата с собой ("Избранное")
        /// </summary>
        private void ConfigureSelfChatAppearance()
        {
            // Принудительно устанавливаем название
            Title.Text = "Избранное";
            _title = "Избранное";

            // Скрываем галочки прочтения
            ReadStatusPanel.Visibility = Visibility.Collapsed;

            // Скрываем онлайн-индикатор
            OnlineIndicator.Visibility = Visibility.Collapsed;
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
        /// Сбрасывает ID первого непрочитанного сообщения
        /// </summary>
        public void ResetFirstUnreadId()
        {
            _firstUnreadId = 0;
        }

        /// <summary>
        /// Устанавливает ID первого непрочитанного сообщения (только если текущий равен 0)
        /// </summary>
        public void SetFirstUnreadIdIfZero(long messageId)
        {
            if (_firstUnreadId == 0)
            {
                _firstUnreadId = messageId;
            }
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
                // Не показываем галочки прочтения для чата с собой
                if (_isSelfChat)
                {
                    ReadStatusPanel.Visibility = Visibility.Collapsed;
                    return;
                }

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
            App.Messenger.OpenChatById(ChatId, _lastMessageId, _isGroupChat, _userId, _title, _firstUnreadId);
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

        #region Online Status

        private void ChatItem_Loaded(object sender, RoutedEventArgs e)
        {
            // Only track online status for non-group chats and not for self-chat
            if (!_isGroupChat && _userId > 0 && !_isSelfChat)
            {
                App.OnlineStatusService.OnlineStatusChanged += OnOnlineStatusChanged;
                App.OnlineStatusService.TrackUser(_userId);

                // Get cached status if available
                var cachedStatus = App.OnlineStatusService.GetCachedStatus(_userId);
                if (cachedStatus != null)
                {
                    UpdateOnlineStatus(
                        cachedStatus.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline,
                        cachedStatus.LastSeen?.ToDateTime()
                    );
                }

                // КРИТИЧНО: Делаем отдельный запрос для немедленного получения статуса
                _ = FetchAndUpdateOnlineStatusAsync(_userId);
            }
        }

        private void ChatItem_Unloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from online status events (only if we were subscribed)
            if (!_isGroupChat && _userId > 0 && !_isSelfChat)
            {
                App.OnlineStatusService.OnlineStatusChanged -= OnOnlineStatusChanged;
                App.OnlineStatusService.UntrackUser(_userId);
            }
        }

        private void OnOnlineStatusChanged(BarkFluff.Proto.Onliner.UserOnlineStatus status)
        {
            if (status.UserId == _userId)
            {
                UpdateOnlineStatus(
                    status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline,
                    status.LastSeen?.ToDateTime()
                );
            }
        }

        /// <summary>
        /// Updates the online status indicator
        /// </summary>
        public void UpdateOnlineStatus(bool isOnline, DateTime? lastSeen)
        {
            _isOnline = isOnline;
            _lastSeen = lastSeen;

            // Не показываем онлайн-статус для чата с собой
            if (_isSelfChat)
            {
                OnlineIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            // ИСПРАВЛЕНИЕ: Используем CheckAccess вместо всегда вызывать Invoke
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateOnlineStatus(isOnline, lastSeen)));
                return;
            }

            // Теперь гарантированно в UI потоке
            // Only show online indicator for non-group chats
            OnlineIndicator.Visibility = (isOnline && !_isGroupChat)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Делает немедленный запрос статуса онлайна для пользователя (не через stream)
        /// </summary>
        private async System.Threading.Tasks.Task FetchAndUpdateOnlineStatusAsync(long userId)
        {
            try
            {
                var userIds = new System.Collections.Generic.List<long> { userId };
                var (error, response) = await App.ServerCommunication.GetOnlineStatus(userIds, App.GParam);

                if (error.IsSuccess && response != null && response.UsersStatuses.Count > 0)
                {
                    var status = response.UsersStatuses[0];

                    // Обновляем кеш в OnlineStatusService для консистентности
                    App.OnlineStatusService.UpdateCachedStatus(status);

                    // Обновляем UI
                    UpdateOnlineStatus(
                        status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline,
                        status.LastSeen?.ToDateTime()
                    );
                }
            }
            catch (System.Exception)
            {
                // Игнорируем ошибки - будем полагаться на stream обновления
            }
        }

        #endregion

        #region Drag and Drop

        /// <summary>
        /// Обрабатывает начало перетаскивания файлов на этот ChatItem
        /// </summary>
        private void UserControl_DragEnter(object sender, DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Перетаскивание: {_title}");

            // Проверяем, содержит ли перетаскиваемый объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    e.Effects = DragDropEffects.Copy;
                    _isDraggingFiles = true;
                    ShowDragDropOverlay();
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        /// <summary>
        /// Обрабатывает непрерывное перетаскивание над ChatItem
        /// </summary>
        private void UserControl_DragOver(object sender, DragEventArgs e)
        {
            // Поддерживаем эффект копирования при наведении
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;

                // Если overlay по какой-то причине скрыт, но мы над контролом - показываем его
                if (!_isDraggingFiles && DragDropVisualOverlay != null && DragDropVisualOverlay.Visibility != Visibility.Visible)
                {
                    _isDraggingFiles = true;
                    ShowDragDropOverlay();
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        /// <summary>
        /// Обрабатывает уход курсора с перетаскиваемыми файлами от ChatItem
        /// </summary>
        private void UserControl_DragLeave(object sender, DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"ChatItem DragLeave: {_title}");

            // Всегда скрываем overlay при DragLeave
            // DragOver восстановит его, если курсор все еще над контролом
            _isDraggingFiles = false;
            HideDragDropOverlay();

            e.Handled = true;
        }

        /// <summary>
        /// Обрабатывает отпускание файлов на ChatItem
        /// </summary>
        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"ChatItem Drop: {_title}");

            _isDraggingFiles = false;
            HideDragDropOverlay();

            // Проверяем, содержит ли сброшенный объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Сброшено {files.Length} файлов на чат: {_title}");

                    // Открываем чат и показываем превью вложений
                    App.Messenger.OpenChatAndShowAttachments(
                        ChatId,
                        _lastMessageId,
                        _isGroupChat,
                        _userId,
                        _title,
                        files.ToList()
                    );
                }
            }

            e.Handled = true;
        }

        /// <summary>
        /// Показывает визуальный overlay при drag & drop
        /// </summary>
        private void ShowDragDropOverlay()
        {
            Dispatcher.Invoke(() =>
            {
                if (DragDropVisualOverlay == null) return;

                // Скрываем основной контент чата
                if (BaseChatGrid != null)
                {
                    BaseChatGrid.Visibility = Visibility.Collapsed;
                }

                // Обновляем контент overlay данными текущего чата
                if (DragDropAvatar != null)
                {
                    DragDropAvatar.FileId = _avatarFileId;
                    DragDropAvatar.FileUrl = _url;
                }

                // Отменяем любые текущие анимации
                DragDropVisualOverlay.BeginAnimation(UIElement.OpacityProperty, null);

                // Показываем overlay
                DragDropVisualOverlay.Visibility = Visibility.Visible;

                // Если уже видим, просто устанавливаем полную непрозрачность
                if (DragDropVisualOverlay.Opacity > 0.5)
                {
                    DragDropVisualOverlay.Opacity = 1.0;
                }
                else
                {
                    // Плавное появление только если был скрыт
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = DragDropVisualOverlay.Opacity,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(150)
                    };
                    DragDropVisualOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
            });
        }

        /// <summary>
        /// Скрывает визуальный overlay drag & drop
        /// </summary>
        private void HideDragDropOverlay()
        {
            Dispatcher.Invoke(() =>
            {
                if (DragDropVisualOverlay == null) return;

                // Отменяем любые текущие анимации
                DragDropVisualOverlay.BeginAnimation(UIElement.OpacityProperty, null);

                // Плавное исчезновение
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = DragDropVisualOverlay.Opacity,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(100)
                };

                fadeOut.Completed += (s, e) =>
                {
                    // Проверяем, что за время анимации не началось новое перетаскивание
                    if (!_isDraggingFiles)
                    {
                        DragDropVisualOverlay.Visibility = Visibility.Collapsed;

                        // Показываем обратно основной контент чата
                        if (BaseChatGrid != null)
                        {
                            BaseChatGrid.Visibility = Visibility.Visible;
                        }
                    }
                };

                DragDropVisualOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            });
        }

        #endregion
    }
}
