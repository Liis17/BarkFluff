using BarkFluff.Client.WPF.Reactive;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.Services.Notification;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MessageAttachmentType = BarkFluff.Proto.Shared.MessageAttachmentType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;
namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для MessengerPage.xaml
    /// </summary>
    public partial class MessengerPage : UserControl
    {
        public ReactiveBool IsOpenChat { get; set; } = new ReactiveBool(false);
        public ReactiveString ChatId { get; set; } = new ReactiveString(string.Empty);
        public string TitleChat { get; set; } = string.Empty;
        private long _openedLastMessageId { get; set; } = 0;
        private long _oldestLoadedMessageId { get; set; } = 0;
        private long _firstUnreadMessageId = 0;
        private bool _isLoadingHistory = false;
        private bool _hasMoreHistory = true;

        // Константы для обработки сообщений
        private const int HISTORY_PAGE_SIZE = 30;
        private const int SCROLL_TOP_THRESHOLD = 100;
        private const int MARK_AS_READ_DEBOUNCE_MS = 1000;
        private const int INITIAL_MARK_DELAY_MS = 500;

        public bool IsOpenChatEmpty { get; set; } = false;
        public ReactiveLong ChatIdbyUserId { get; set; } = new ReactiveLong(0);
        public bool IsGroup { get; set; } = false;
        private string? _currentChatAvatarFileId;
        private string? _currentUserAvatarFileId;

        // Для отслеживания онлайн статуса текущего открытого чата
        private long _currentChatUserId = 0;
        private bool _isCurrentChatGroup = false;

        // Буфер для хранения chatId -> lastMessageId для быстрого обновления статуса прочтения
        private readonly Dictionary<string, long> _chatLastMessageBuffer = new();
        private readonly object _chatBufferLock = new();

        public MessengerPage()
        {
            InitializeComponent();

            Loaded += MessengerPage_Loaded;
            Unloaded += MessengerPage_Unloaded;

            SubscribeToReactiveProperties();
            StartSlideDownAndFadeIn();

            // Подписываемся на событие прокрутки для загрузки истории
            MessageScrollViewer.ScrollChanged += MessageScrollViewer_ScrollChanged;

            // Подписываемся на события превью вложений
            AttachmentPreview.OnCancel += AttachmentPreview_OnCancel;
            AttachmentPreview.OnSend += AttachmentPreview_OnSend;

            // Перехватываем команду вставки для обработки файлов и изображений
            var pasteBinding = new CommandBinding(ApplicationCommands.Paste);
            pasteBinding.Executed += OnTextForMessagePasteCommand;
            TextForMessage.CommandBindings.Add(pasteBinding);
        }

        private async void MessageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            // Проверяем, прокручено ли до верха (с порогом)
            if (scrollViewer.VerticalOffset < SCROLL_TOP_THRESHOLD && !_isLoadingHistory && _hasMoreHistory && !string.IsNullOrEmpty(ChatId.Value))
            {
                await LoadHistoryMessages();
            }
        }

        /// <summary>
        /// Загружает более старые сообщения при прокрутке к верху
        /// </summary>
        private async Task LoadHistoryMessages()
        {
            if (_isLoadingHistory || !_hasMoreHistory || _oldestLoadedMessageId == 0) return;

            _isLoadingHistory = true;

            try
            {
                App.ErideMessage.AddMessage($"Загрузка истории сообщений (from ID: {_oldestLoadedMessageId})", new Erida { Type = MType.Debug });

                // Сначала пробуем загрузить из кеша
                var cachedMessages = App.CacheManager.GetMessages(ChatId.Value, _oldestLoadedMessageId, HISTORY_PAGE_SIZE);
                var hasLoadedFromCache = false;

                if (cachedMessages != null && cachedMessages.Count > 0)
                {
                    App.ErideMessage.AddMessage($"Загружено {cachedMessages.Count} сообщений из кеша", new Erida { Type = MType.Debug });
                    await InsertHistoryMessages(cachedMessages);
                    hasLoadedFromCache = true;
                }

                // Затем загружаем с сервера
                var response = await App.ServerCommunication.GetMessagesWithOffset(
                    App.GParam,
                    ChatId.Value,
                    _oldestLoadedMessageId,
                    HISTORY_PAGE_SIZE,
                    0);

                if (response.error.IsSuccess && response.messages != null && response.messages.Count > 0)
                {
                    App.ErideMessage.AddMessage($"Загружено {response.messages.Count} сообщений с сервера", new Erida { Type = MType.Debug });

                    // Сохраняем в кеш
                    foreach (var msg in response.messages)
                    {
                        App.CacheManager.SaveMessage(ChatId.Value, TitleChat, msg, MessageOperation.Added);
                    }

                    // Вставляем в UI (будет удаление дубликатов)
                    await InsertHistoryMessages(response.messages);

                    // Если получено меньше сообщений, чем запрашивали - значит достигнут конец
                    if (response.messages.Count < HISTORY_PAGE_SIZE)
                    {
                        _hasMoreHistory = false;
                        App.ErideMessage.AddMessage("Достигнут конец истории сообщений", new Erida { Type = MType.Debug });
                    }
                }
                else if (response.error.ErrorCode == 1)
                {
                    // Нет больше сообщений
                    _hasMoreHistory = false;
                    App.ErideMessage.AddMessage("Нет больше сообщений для загрузки", new Erida { Type = MType.Debug });
                }
                else if (!hasLoadedFromCache)
                {
                    App.ErideMessage.AddMessage($"Ошибка загрузки истории: {response.error.ErrorMessage}", new Erida { Type = MType.Error });
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Исключение при загрузке истории: {ex.Message}", new Erida { Type = MType.Error });
            }
            finally
            {
                _isLoadingHistory = false;
            }
        }

        /// <summary>
        /// Вставляет сообщения истории в начало области сообщений с удалением дубликатов
        /// </summary>
        private async Task InsertHistoryMessages(List<MessageModel> messages)
        {
            if (messages == null || messages.Count == 0) return;

            await Dispatcher.InvokeAsync(() =>
            {
                // Запоминаем текущую позицию прокрутки
                var scrollViewer = MessageScrollViewer;
                var oldScrollHeight = scrollViewer.ScrollableHeight;
                var oldOffset = scrollViewer.VerticalOffset;

                // Получаем существующие ID сообщений для удаления дубликатов
                var existingMessageIds = new HashSet<long>();
                foreach (var child in MessageArea.Children)
                {
                    if (child is MessageBubble bubble && long.TryParse(bubble.MessageId, out long id))
                    {
                        existingMessageIds.Add(id);
                    }
                }

                // Сортируем сообщения по времени (сначала старые для корректной вставки)
                var sortedMessages = messages.OrderBy(m => m.SentAt.ToDateTime()).ToList();
                var insertedCount = 0;

                foreach (var message in sortedMessages)
                {
                    // Пропускаем дубликаты
                    if (existingMessageIds.Contains(message.MessageId))
                    {
                        continue;
                    }

                    // Обновляем ID самого старого загруженного сообщения
                    if (message.MessageId < _oldestLoadedMessageId || _oldestLoadedMessageId == 0)
                    {
                        _oldestLoadedMessageId = message.MessageId;
                    }

                    var owner = message.SenderId == App.GParam.UserId ? MessageBubble.MessageOwner.Me : MessageBubble.MessageOwner.Interlocutor;
                    var type = GetMessageType(message);
                    var messageItem = new MessageBubble(owner, type, message, IsGroup);

                    // Вставляем в начало (сначала самые старые сообщения)
                    MessageArea.Children.Insert(insertedCount, messageItem);
                    insertedCount++;
                }

                if (insertedCount > 0)
                {
                    // Восстанавливаем позицию прокрутки (с учётом нового содержимого)
                    scrollViewer.UpdateLayout();
                    var newScrollHeight = scrollViewer.ScrollableHeight;
                    var scrollDifference = newScrollHeight - oldScrollHeight;
                    scrollViewer.ScrollToVerticalOffset(oldOffset + scrollDifference);

                    App.ErideMessage.AddMessage($"Добавлено {insertedCount} сообщений в историю", new Erida { Type = MType.Debug });
                }
            });
        }

        private void SubscribeToReactiveProperties()
        {
            IsOpenChat.PropertyChanged += IsOpenChat_PropertyChanged;
            ChatId.PropertyChanged += ChatId_PropertyChanged;
            ChatIdbyUserId.PropertyChanged += ChatIdbyUserId_PropertyChanged;
        }

        private void UnsubscribeFromReactiveProperties()
        {
            IsOpenChat.PropertyChanged -= IsOpenChat_PropertyChanged;
            ChatId.PropertyChanged -= ChatId_PropertyChanged;
            ChatIdbyUserId.PropertyChanged -= ChatIdbyUserId_PropertyChanged;
        }

        private void MessengerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromReactiveProperties();

            // Отписываемся от событий кеширования
            App.FileCacheService.FileCached -= OnFileCached;

            // Отписываемся от событий клика по уведомлениям
            App.NotificationManager.NotificationClicked -= OnNotificationClicked;

            // Отписываемся от обновлений онлайн статуса
            App.OnlineStatusService.OnlineStatusChanged -= OnOnlineStatusChanged_MessengerPage;

            // Отписываемся от подписок сервиса реального времени
            CleanupRealtimeService();

            IsOpenChat?.Dispose();
            ChatId?.Dispose();
            ChatIdbyUserId?.Dispose();
        }

        private void OnFileCached(string fileId, string filePath, FileType fileType)
        {
            if (fileType != FileType.Avatar) return;

            Dispatcher.Invoke(() =>
            {
                if (fileId == _currentChatAvatarFileId)
                {
                    SetChatAvatarImage(filePath);
                }
                if (fileId == _currentUserAvatarFileId)
                {
                    SetTitleWindowAvatarImage(filePath);
                }
            });
        }

        private void SetChatAvatarImage(string imagePath)
        {
            try
            {
                ChatAvatar.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
        }

        private void SetTitleWindowAvatarImage(string imagePath)
        {
            try
            {
                AvatarTitleWindow.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
        }

        #region Обработка онлайн статусов

        private void OnOnlineStatusChanged_MessengerPage(BarkFluff.Proto.Onliner.UserOnlineStatus status)
        {
            // Обновляем только если это пользователь текущего открытого чата
            if (!_isCurrentChatGroup && status.UserId == _currentChatUserId)
            {
                UpdateUserOnlineTime(status);
            }
        }

        private void UpdateUserOnlineTime(BarkFluff.Proto.Onliner.UserOnlineStatus status)
        {
            // События уже приходят в UI потоке после исправления ProcessStatusUpdate, но проверяем на всякий случай
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateUserOnlineTime(status)));
                return;
            }

            // Найти TextBlock UserOnlineTime в XAML
            if (UserOnlineTime == null) return;

            if (status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline)
            {
                UserOnlineTime.Text = "В сети";
                UserOnlineTime.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(68, 214, 44));
            }
            else if (status.LastSeen != null)
            {
                var lastSeen = status.LastSeen.ToDateTime();
                UserOnlineTime.Text = FormatLastSeenForMessenger(lastSeen);
                UserOnlineTime.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(128, 255, 255, 255));
            }
            else
            {
                UserOnlineTime.Text = string.Empty;
            }
        }

        private string FormatLastSeenForMessenger(DateTime lastSeen)
        {
            // Проверяем если время Unknown (Unix epoch или очень старая дата)
            // gRPC Google.Protobuf.WellKnownTypes.Timestamp может вернуть 1970-01-01 для Unknown
            if (lastSeen.Year <= 1970 || lastSeen == DateTime.MinValue)
            {
                return "Был(а) давно";
            }

            var localTime = lastSeen.ToLocalTime();
            var now = DateTime.Now;

            // Если это сегодня - показываем только время
            if (localTime.Date == now.Date)
            {
                return $"Был(а) в {localTime:HH:mm}";
            }
            // Если это вчера
            else if (localTime.Date == now.Date.AddDays(-1))
            {
                return $"Был(а) вчера в {localTime:HH:mm}";
            }
            // Если это в пределах недели
            else if ((now - localTime).TotalDays < 7)
            {
                string dayName = localTime.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture);
                return $"Был(а) в {dayName} в {localTime:HH:mm}";
            }
            // Старше недели
            else
            {
                return $"Был(а) {localTime:dd.MM.yyyy} в {localTime:HH:mm}";
            }
        }

        /// <summary>
        /// Делает немедленный запрос статуса онлайна для пользователя (не через stream)
        /// </summary>
        private async Task FetchAndUpdateOnlineStatus(long userId)
        {
            try
            {
                var userIds = new List<long> { userId };
                var (error, response) = await App.ServerCommunication.GetOnlineStatus(userIds, App.GParam);

                if (error.IsSuccess && response != null && response.UsersStatuses.Count > 0)
                {
                    var status = response.UsersStatuses[0];

                    // Обновляем кеш в OnlineStatusService для консистентности с stream обновлениями
                    App.OnlineStatusService.UpdateCachedStatus(status);

                    // Обновляем UI
                    Dispatcher.Invoke(() =>
                    {
                        UpdateUserOnlineTime(status);
                    });

                    App.ErideMessage.AddMessage(
                        $"Получен статус для пользователя {userId}: {(status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline ? "онлайн" : "оффлайн")}",
                        new Erida { Type = MType.Debug });
                }
                else
                {
                    // Если не удалось получить статус - показываем пустое
                    Dispatcher.Invoke(() =>
                    {
                        if (UserOnlineTime != null)
                        {
                            UserOnlineTime.Text = string.Empty;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage(
                    $"Ошибка получения статуса для пользователя {userId}: {ex.Message}",
                    new Erida { Type = MType.Error });
            }
        }

        #endregion

        #region Обработчики событий
        private async void ChatIdbyUserId_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ChatIdbyUserId.Value == 0) { return; } //если chatId пустой, то выходим из метода
            IsOpenChat.Value = true;
            IsOpenChatEmpty = true;
            IsGroup = false;
            ChatId.Value = string.Empty;
            App.ErideMessage.AddMessage($"Открытие чата с UserID: {ChatIdbyUserId.Value}", new Erida { Type = MType.Debug });
            _openedLastMessageId = 0;
            _oldestLoadedMessageId = 0;
            _hasMoreHistory = true;
            MessageArea.Children.Clear();
            GetChatInfo(ChatIdbyUserId.Value); // получаем информацию о чате для вывода в заголовке и аватара

            // Обновление онлайн статуса для открытого чата
            _currentChatUserId = ChatIdbyUserId.Value;
            _isCurrentChatGroup = false;

            // Трекаем пользователя и получаем его онлайн статус
            if (ChatIdbyUserId.Value > 0)
            {
                App.OnlineStatusService.TrackUser(ChatIdbyUserId.Value);

                var cachedStatus = App.OnlineStatusService.GetCachedStatus(ChatIdbyUserId.Value);
                if (cachedStatus != null)
                {
                    UpdateUserOnlineTime(cachedStatus);
                }
                else
                {
                    if (UserOnlineTime != null)
                    {
                        UserOnlineTime.Text = string.Empty;
                    }
                }
            }
        }
        private void TextForMessage_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                //var textBox = sender as TextBox;
                //textBox.Focus();
            }
            catch { } // игнорируем ошибку если не получилось сфокусироваться на текстбоксе

        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
            {
                var textBox = sender as TextBox;
                textBox.AcceptsReturn = true;
            }
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                tempMessage = textBox.Text;
                var a = tempMessage.Replace(" ", "");
                if (a == "")
                {
                    return;
                }
                SendMessage(sender, null);
                textBox.Text = string.Empty;
                textBox.AcceptsReturn = false;
            }
        }

        private void TextForMessage_KeyUp(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
            {
                textBox.AcceptsReturn = false;
            }
        }

        private async void ChatId_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            MessageArea.Children.Clear();
            GC.Collect();

            if (string.IsNullOrEmpty(ChatId.Value) || IsOpenChatEmpty || _openedLastMessageId == 0)
            {
                return;
            }
            ChatIdbyUserId.Value = 0;
            App.ErideMessage.AddMessage($"Загрузка сообщений чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });

            // Сначала показываем кешированные сообщения
            var cachedMessages = App.CacheManager.GetMessages(ChatId.Value, _openedLastMessageId, 50);
            if (cachedMessages != null && cachedMessages.Count > 0)
            {
                App.ErideMessage.AddMessage($"Показываем {cachedMessages.Count} кешированных сообщений", new Erida { Type = MType.Debug });
                DisplayMessages(cachedMessages, _firstUnreadMessageId != 0);
            }

            // Затем загружаем актуальные сообщения с сервера
            var response = await App.ServerCommunication.GetMessages(App.GParam, ChatId.Value, _openedLastMessageId);
            if (!response.error.IsSuccess)
            {
                if (response.error.ErrorCode != 1)
                {
                    App.ErideMessage.AddMessage($"Ошибка при открытии чата: {response.error.ErrorMessage}", new Erida { Type = MType.Error });
                    return;
                }
            }

            if (response.messages != null && response.messages.Count > 0)
            {
                // Сохраняем сообщения в кеш
                foreach (var msg in response.messages)
                {
                    App.CacheManager.SaveMessage(ChatId.Value, TitleChat, msg, MessageOperation.Added);
                }

                DisplayMessages(response.messages, _firstUnreadMessageId != 0);
            }

            // Глобальная подписка на уведомления о прочтении уже запущена в ProcessMessages
        }

        private void DisplayMessages(List<MessageModel> messages, bool scrollToFirstUnread = false)
        {
            if (messages == null || messages.Count == 0)
            {
                return;
            }

            MessageArea.Children.Clear();
            var sortedMessages = messages.OrderBy(m => m.SentAt.ToDateTime()).ToList();

            // Track the oldest message ID for pagination
            if (sortedMessages.Count > 0)
            {
                _oldestLoadedMessageId = sortedMessages.First().MessageId;
                _hasMoreHistory = true; // Reset history flag when opening a new chat
            }

            // Группировка по дням (используем ЛОКАЛЬНОЕ время для правильного отображения)
            var groupedMessages = sortedMessages.GroupBy(m => m.SentAt.ToDateTime().ToLocalTime().Date)
                                              .OrderBy(g => g.Key);

            bool foundFirstUnread = false;

            foreach (var group in groupedMessages)
            {
                // Добавляем контрол с датой по центру
                var dateHeader = GetDateHeader(group.Key);
                var dateControl = new DateHeaderControl { Text = dateHeader };
                dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                dateControl.Margin = new Thickness(0, 10, 0, 10);
                AddMessageWithoutScroll(dateControl);

                // Добавляем сообщения группы
                foreach (var item in group)
                {
                    var owner = item.SenderId == App.GParam.UserId ? MessageBubble.MessageOwner.Me : MessageBubble.MessageOwner.Interlocutor;
                    var type = GetMessageType(item);
                    var messageItem = new MessageBubble(owner, type, item, IsGroup);

                    // Если это первое непрочитанное - добавляем разделитель ПЕРЕД ним
                    if (scrollToFirstUnread && !foundFirstUnread && item.MessageId == _firstUnreadMessageId)
                    {
                        foundFirstUnread = true;
                        AddUnreadSeparator();
                    }

                    // Добавляем сообщение БЕЗ автоматического скролла
                    AddMessageWithoutScroll(messageItem);
                }
            }

            // Выполняем скролл после добавления всех сообщений
            if (scrollToFirstUnread && _firstUnreadMessageId != 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ScrollToMessage(_firstUnreadMessageId);
                    // После скролла отмечаем видимые сообщения как прочитанные
                    MarkVisibleMessagesAsRead();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                MessageScrollViewer.ScrollToEnd();
                // При обычной загрузке тоже отмечаем сообщения
                MarkVisibleMessagesAsRead();
            }
        }

        private static MessageBubble.MessageType GetMessageType(MessageModel message)
        {
            if (message.Attachments == null || message.Attachments.Count == 0)
            {
                return MessageBubble.MessageType.Text;
            }

            var firstAttachment = message.Attachments.FirstOrDefault();
            if (firstAttachment == null)
            {
                return MessageBubble.MessageType.Text;
            }

            return firstAttachment.Type switch
            {
                MessageAttachmentType.Image => MessageBubble.MessageType.Image,
                MessageAttachmentType.Video => MessageBubble.MessageType.Video,
                MessageAttachmentType.Gif => MessageBubble.MessageType.Gif,
                MessageAttachmentType.Document => MessageBubble.MessageType.Document,
                _ => MessageBubble.MessageType.Text
            };
        }

        private string GetDateHeader(DateTime date)
        {
            if (date.Date == DateTime.Today) return "Сегодня";
            if (date.Date == DateTime.Today.AddDays(-1)) return "Вчера";
            return date.ToString("dd.MM.yyyy");
        }

        /// <summary>
        /// Добавляет разделитель непрочитанных сообщений
        /// </summary>
        private void AddUnreadSeparator()
        {
            var separator = new UnreadSeparatorControl
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };
            AddMessageWithoutScroll(separator);
        }

        /// <summary>
        /// Прокручивает чат к сообщению с указанным ID, центрируя его на экране
        /// </summary>
        private void ScrollToMessage(long messageId)
        {
            MessageBubble targetBubble = null;
            foreach (var child in MessageArea.Children)
            {
                if (child is MessageBubble bubble && long.TryParse(bubble.MessageId, out long id) && id == messageId)
                {
                    targetBubble = bubble;
                    break;
                }
            }

            if (targetBubble != null)
            {
                MessageArea.UpdateLayout();
                MessageScrollViewer.UpdateLayout();

                var position = targetBubble.TransformToVisual(MessageArea)
                                           .Transform(new Point(0, 0));
                var targetOffset = position.Y - (MessageScrollViewer.ActualHeight / 2) + (targetBubble.ActualHeight / 2);
                var clampedOffset = Math.Max(0, targetOffset);

                var animation = new DoubleAnimation
                {
                    From = MessageScrollViewer.VerticalOffset,
                    To = clampedOffset,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var story = new Storyboard();
                story.Children.Add(animation);
                Storyboard.SetTarget(animation, MessageScrollViewer);
                Storyboard.SetTargetProperty(animation, new PropertyPath(ScrollAnimationBehavior.VerticalOffsetProperty));

                story.Completed += (s, e) =>
                {
                    MessageScrollViewer.ScrollToVerticalOffset(clampedOffset);
                };

                story.Begin();
            }
        }

        private void IsOpenChat_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (IsOpenChat.Value)
            {
                OpenedChat.Visibility = Visibility.Visible;
            }
            else
            {
                OpenedChat.Visibility = Visibility.Collapsed;
            }
        }

        private async void MessengerPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Подписываемся на события кеширования
            App.FileCacheService.FileCached += OnFileCached;

            // Подписываемся на события клика по уведомлениям
            App.NotificationManager.NotificationClicked += OnNotificationClicked;

            // Подписываемся на обновления онлайн статуса
            App.OnlineStatusService.OnlineStatusChanged += OnOnlineStatusChanged_MessengerPage;

            //вызом метода для подписки на обновление приложения
            App.MainPageLoaded();

            // Устанавливаем placeholder для аватарок
            ChatAvatar.ImageSource = null;
            AvatarTitleWindow.ImageSource = null;

            OpenedChat.Visibility = Visibility.Collapsed;
            App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);
            var (error, serverInfo) = await App.ServerCommunication.GetServerInfo(App.GParam);
            if (!error.IsSuccess)
            {
                App.ErideMessage.AddMessage(error.ErrorMessage, new Erida { Type = MType.Error });
                return;
            }
            else
            {
                App.ErideMessage.AddMessage("Получена информация о сервере", new Erida { Type = MType.Debug });

                App.GParam.ServerName = serverInfo.Name;
                App.GParam.ServerDescription = serverInfo.Description;
                App.GParam.SocketIdentity = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Identity.Endpoint.Host + ":" + serverInfo.Identity.Endpoint.Port);
                App.GParam.SocketUsers = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Users.Endpoint.Host + ":" + serverInfo.Users.Endpoint.Port);
                App.GParam.SocketFiles = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Files.Endpoint.Host + ":" + serverInfo.Files.Endpoint.Port);
                App.GParam.SocketMessages = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Messages.Endpoint.Host + ":" + serverInfo.Messages.Endpoint.Port);
                App.GParam.SocketUpdates = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Updates.Endpoint.Host + ":" + serverInfo.Updates.Endpoint.Port);
                App.GParam.SocketOnliner = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Onliner.Endpoint.Host + ":" + serverInfo.Onliner.Endpoint.Port);
                App.GParam.Colors = new ClientColors()
                {
                    LiteHex = serverInfo.Color.LiteHex,
                    MainHex = serverInfo.Color.MainHex,
                    HardHex = serverInfo.Color.HardHex,
                };
                MainWindow.SaveSettings();
            }

            var response = App.ServerCommunication.CreateAC(App.GParam, App.GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, App.GParam.IpAddress);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage(response.ErrorMessage ?? "Неизвестная проблема", new Erida { Type = MType.Error });
                return;
            }
            else
            {
                App.ErideMessage.AddMessage("API клиент успешно обновлён", new Erida { Type = MType.Debug });
            }


            UserInfoUpdate();
            ChatUpdate();
            await Task.Run(() => ProcessMessages(App.GParam));

            // Выполнение задачи от протокола

            App.MessagerTask.PropertyChanged += MessagerTask_PropertyChanged;

            var task = App.MessagerTask.Value;


            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenCommandViaProtocol(task);
            }
        }

        private async void MessagerTask_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var task = App.MessagerTask.Value;
            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenCommandViaProtocol(task);
            }
            else
            {
                return;
            }




        }
        #endregion

        #region Обработчка аргументов из протокола

        private async void OpenCommandViaProtocol(string task)
        {
            try
            {
                if (task.StartsWith("user-username"))
                {
                    var command = task.Split("=")[0];
                    var arg = task.Split("=")[1];
                    if (command == "user-username")
                    {
                        var result = await App.ServerCommunication.CheckUsername(arg, App.GParam);
                        if (!result.error.IsSuccess)
                        {
                            App.ErideMessage.AddMessage($"Что-то пошло не так {result.error.ErrorMessage}", new Erida { Type = MType.Warning });
                            return;
                        }
                        if (result.exists)
                        {
                            var responseUserId = await App.ServerCommunication.SearchUser(App.GParam, arg);
                            if (!responseUserId.error.IsSuccess)
                            {
                                App.ErideMessage.AddMessage($"Что-то пошло не так {responseUserId.error.ErrorMessage}", new Erida { Type = MType.Warning });
                                return;
                            }
                            var responseChatId = await App.ServerCommunication.GetPersonChatId(App.GParam, responseUserId.userList[0].Id);
                            if (!responseChatId.error.IsSuccess)
                            {
                                App.ErideMessage.AddMessage($"Что-то пошло не так {responseChatId.error.ErrorMessage}", new Erida { Type = MType.Warning });
                                return;
                            }
                            IsOpenChatEmpty = true;
                            IsOpenChat.Value = true;
                            ChatId.Value = responseChatId.chatId;
                            OpenChatFromSearch(responseUserId.userList[0].Id, "чат тайтл");
                            App.ErideMessage.AddMessage($"Открытие чата с {arg}", new Erida { Type = MType.Warning });
                        }
                        else
                        {
                            App.ErideMessage.AddMessage($"Пользователь {arg} не найден", new Erida { Type = MType.Warning });
                        }
                    }
                }
                if (task.StartsWith("successfulupdate"))
                {
                    OpenCenterPanel();
                    CenterPanel.Child = new UserControls.PostUpdateMessage();
                }

                if (task.StartsWith("launch-updater"))
                {
                    // Показать сообщение об обновлении
                    var message = "Доступно новое обновление Barkfluff!";
                    App.ErideMessage.AddMessage(message, new Erida { Type = MType.Warning });

                    // Запустить Barkfluff.Updater.CLI.exe
                    try
                    {
                        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        string updaterPath = System.IO.Path.Combine(appDirectory, "Barkfluff.Updater.CLI.exe");

                        if (System.IO.File.Exists(updaterPath))
                        {
                            // Запустить обновление в фоновом режиме
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = updaterPath,
                                Arguments = "--noseamless", // Принудительное обновление без бесшовного режима
                                CreateNoWindow = true, // Не показывать окно консоли
                                WindowStyle = ProcessWindowStyle.Hidden,
                                UseShellExecute = true // Использовать оболочку для запуска
                            });

                            App.ErideMessage.AddMessage("Запущено обновление Barkfluff", new Erida { Type = MType.Debug });
                        }
                        else
                        {
                            App.ErideMessage.AddMessage("Обновление не найдено", new Erida { Type = MType.Warning });
                        }
                    }
                    catch (Exception ex)
                    {
                        App.ErideMessage.AddMessage($"Ошибка при запуске обновления: {ex.Message}", new Erida { Type = MType.Error });
                    }
                }

            }
            catch (Exception ex)
            {
                var a = ex.Message;
                MessageBox.Show("Ошибка при выполнении задачи из протокола");
            }
        }

        #endregion


        #region Сообщения
        private const int MESSAGE_LIMIT = 4096; // лимит символов в одном сообщении
        private string tempMessage; // временное хранение сообщения
        private List<string> attachedFiles { get; set; } = new List<string>(); //список ID прикрепленных файлов
        private void SendMessage(object sender, RoutedEventArgs e)
        {
            tempMessage = TextForMessage.Text;

            if (string.IsNullOrEmpty(tempMessage)) return;

            List<string> messageParts = SplitMessage(tempMessage, MESSAGE_LIMIT);
            TextForMessage.Text = string.Empty;
            foreach (var part in messageParts)
            {
                string resipientId = "0";
                bool isUserId = false;
                if (IsOpenChatEmpty)
                {
                    resipientId = ChatIdbyUserId.Value.ToString();
                    isUserId = true;
                }
                else
                {
                    resipientId = ChatId.Value;
                    isUserId = false;
                }

                (bool, bool, string) options = new(true, isUserId, resipientId);
                var messageControl = new MessageBubble(part, options, attachedFiles);
                var message = new MessageModel
                {
                    Text = part,
                    ChatId = ChatId.Value,
                    SenderId = App.GParam.UserId,
                    SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                };

                // Проверяем и добавляем разделитель даты перед добавлением сообщения
                AddDateSeparatorIfNeeded(DateTime.Now);

                AddMessage(messageControl);
                UpdateChatWithMessage(message);
            }
        }
        private void AddMessage(UserControl control)
        {
            tempMessage = string.Empty;
            MessageArea.Children.Add(control);
            var animation = (Storyboard)FindResource("MessageAppearAnimation");
            Storyboard.SetTarget(animation, control);
            animation.Begin();
            MessageScrollViewer.ScrollToEnd();

            // Отметить видимые сообщения как прочтенные с небольшой задержкой
            System.Windows.Threading.DispatcherTimer delayTimer = new System.Windows.Threading.DispatcherTimer();
            delayTimer.Interval = TimeSpan.FromMilliseconds(INITIAL_MARK_DELAY_MS);
            delayTimer.Tick += (s, args) =>
            {
                delayTimer.Stop();
                MarkVisibleMessagesAsRead();
            };
            delayTimer.Start();
        }

        /// <summary>
        /// Добавляет сообщение в область сообщений без автоматического скролла
        /// Используется при начальной загрузке сообщений для позиционирования скролла
        /// </summary>
        private void AddMessageWithoutScroll(UserControl control)
        {
            MessageArea.Children.Add(control);
            var animation = (Storyboard)FindResource("MessageAppearAnimation");
            Storyboard.SetTarget(animation, control);
            animation.Begin();
        }

        private System.Windows.Threading.DispatcherTimer? _markAsReadDebounceTimer;
        private HashSet<long> _pendingMarkAsRead = new HashSet<long>();

        /// <summary>
        /// Отмечает видимые входящие сообщения как прочитанные с дебаунсом
        /// </summary>
        private async void MarkVisibleMessagesAsRead()
        {
            if (string.IsNullOrEmpty(ChatId.Value)) return;

            var messagesToMark = new List<long>();

            foreach (var child in MessageArea.Children)
            {
                if (child is MessageBubble bubble)
                {
                    // Отмечаем только входящие сообщения (не от текущего пользователя)
                    if (bubble.SenderId != App.GParam.UserId &&
                        !bubble.ReadBy.Contains(App.GParam.UserId) &&
                        long.TryParse(bubble.MessageId, out long messageId))
                    {
                        messagesToMark.Add(messageId);
                    }
                }
            }

            if (messagesToMark.Count > 0)
            {
                foreach (var id in messagesToMark)
                {
                    _pendingMarkAsRead.Add(id);
                }

                // Дебаунс вызова API
                if (_markAsReadDebounceTimer == null)
                {
                    _markAsReadDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                    _markAsReadDebounceTimer.Interval = TimeSpan.FromMilliseconds(MARK_AS_READ_DEBOUNCE_MS);
                    _markAsReadDebounceTimer.Tick += async (s, args) =>
                    {
                        _markAsReadDebounceTimer.Stop();

                        if (_pendingMarkAsRead.Count > 0)
                        {
                            var idsToMark = _pendingMarkAsRead.ToList();
                            _pendingMarkAsRead.Clear();

                            // Вызвать API для отметки как прочитано
                            await ReadMessage(idsToMark);

                            // Обновить локальный UI
                            foreach (var child in MessageArea.Children)
                            {
                                if (child is MessageBubble bubble && long.TryParse(bubble.MessageId, out long mid) && idsToMark.Contains(mid))
                                {
                                    var newReadBy = bubble.ReadBy.ToList();
                                    if (!newReadBy.Contains(App.GParam.UserId))
                                    {
                                        newReadBy.Add(App.GParam.UserId);
                                        bubble.UpdateReadByList(newReadBy);
                                    }
                                }
                            }

                            // Сбросить счётчик непрочитанных для текущего чата
                            foreach (var child in ChatList.Children)
                            {
                                if (child is ChatItem chatItem && chatItem.ChatId == ChatId.Value)
                                {
                                    chatItem.ResetUnreadCount();
                                    chatItem.ResetFirstUnreadId();
                                    break;
                                }
                            }
                        }
                    };
                }

                _markAsReadDebounceTimer.Stop();
                _markAsReadDebounceTimer.Start();
            }
        }
        private async Task ReadMessage(List<long> messageIds)
        {
            var response = await App.ServerCommunication.MarkMessageAsRead(App.GParam, messageIds);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage("Ошибка при отметке сообщения как прочитанного: " + response.ErrorMessage, new Erida { Type = MType.Debug });
                return;
            }
        }

        public void UpdateChatWithMessage(MessageModel message)
        {
            if (message == null || string.IsNullOrEmpty(message.ChatId))
            {
                App.ErideMessage.AddMessage("Ошибка: MessageModel или ChatId пустые", new Erida { Type = MType.Error });
                return;
            }

            // Обновляем буфер с новым последним сообщением
            lock (_chatBufferLock)
            {
                _chatLastMessageBuffer[message.ChatId] = message.MessageId;
            }

            // Находим ChatItem с совпадающим ChatId
            ChatItem targetChatItem = null;
            foreach (var child in ChatList.Children)
            {
                if (child is ChatItem chatItem && chatItem.ChatId == message.ChatId)
                {
                    targetChatItem = chatItem;
                    break;
                }
            }

            if (targetChatItem == null)
            {
                App.ErideMessage.AddMessage($"Чат с ID {message.ChatId} не найден в ChatList", new Erida { Type = MType.Debug });
                return;
            }

            // Передаем сообщение и обновляем
            targetChatItem.TransferMessage = message;
            targetChatItem.UpdateMessage();

            // Анимированная сортировка ChatItem по времени последнего сообщения
            AnimateChatItemReordering();
        }

        /// <summary>
        /// Анимирует перемещение ChatItem'ов при изменении их позиции в списке
        /// </summary>
        private async void AnimateChatItemReordering()
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                // Получаем текущие позиции всех ChatItem
                var currentPositions = new Dictionary<ChatItem, double>();
                foreach (var child in ChatList.Children)
                {
                    if (child is ChatItem chatItem)
                    {
                        var transform = chatItem.RenderTransform as TranslateTransform;
                        if (transform == null)
                        {
                            transform = new TranslateTransform();
                            chatItem.RenderTransform = transform;
                        }

                        // Сохраняем текущую визуальную позицию
                        var point = chatItem.TransformToAncestor(ChatList).Transform(new Point(0, 0));
                        currentPositions[chatItem] = point.Y;
                    }
                }

                // Сортируем ChatItem по времени последнего сообщения
                var sortedChatItems = ChatList.Children.OfType<ChatItem>()
                    .OrderByDescending(chatItem => chatItem.TransferMessage?.SentAt.ToDateTime() ?? DateTime.MinValue)
                    .ToList();

                // Проверяем, нужно ли вообще что-то менять
                bool needsReordering = false;
                for (int i = 0; i < sortedChatItems.Count; i++)
                {
                    if (ChatList.Children.IndexOf(sortedChatItems[i]) != i)
                    {
                        needsReordering = true;
                        break;
                    }
                }

                if (!needsReordering)
                {
                    return; // Порядок не изменился
                }

                // Перестраиваем список без анимации (логически)
                ChatList.Children.Clear();
                foreach (var chatItem in sortedChatItems)
                {
                    ChatList.Children.Add(chatItem);
                }

                // Даём время на layout update
                await Task.Delay(10);
                ChatList.UpdateLayout();

                // Вычисляем новые позиции и запускаем анимацию
                foreach (var chatItem in sortedChatItems)
                {
                    if (!currentPositions.ContainsKey(chatItem))
                        continue;

                    var oldPosition = currentPositions[chatItem];
                    var newPoint = chatItem.TransformToAncestor(ChatList).Transform(new Point(0, 0));
                    var newPosition = newPoint.Y;

                    var offset = oldPosition - newPosition;

                    // Если элемент не сдвинулся, пропускаем анимацию
                    if (Math.Abs(offset) < 1)
                        continue;

                    var transform = chatItem.RenderTransform as TranslateTransform;
                    if (transform == null)
                    {
                        transform = new TranslateTransform();
                        chatItem.RenderTransform = transform;
                    }

                    // Устанавливаем начальное смещение (визуально элемент остаётся на старом месте)
                    transform.Y = offset;

                    // Создаём анимацию перемещения к новой позиции
                    var animation = new DoubleAnimation
                    {
                        From = offset,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(400),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    };

                    // Запускаем анимацию
                    transform.BeginAnimation(TranslateTransform.YProperty, animation);
                }
            });
        }

        #endregion


        #region Вспомогательные методы

        private List<string> SplitMessage(string message, int maxLength)
        {
            List<string> parts = new List<string>();
            int currentIndex = 0;

            while (currentIndex < message.Length)
            {
                int length = Math.Min(maxLength, message.Length - currentIndex);
                if (length == 0) break;

                // Проверяем, чтобы не разрывать слово
                if (currentIndex + length < message.Length)
                {
                    int lastSpace = message.LastIndexOf(' ', currentIndex + length - 1, length);
                    if (lastSpace > currentIndex)
                    {
                        length = lastSpace - currentIndex;
                    }
                }

                string part = message.Substring(currentIndex, length);
                parts.Add(part.TrimEnd());
                currentIndex += length;
            }

            return parts;
        }
        public async Task ChatUpdate()
        {
            var response = await App.ServerCommunication.GetChats(App.GParam);
            ChatList.Children.Clear(); // Очищаем список перед добавлением

            // Очищаем буфер последних сообщений чатов
            lock (_chatBufferLock)
            {
                _chatLastMessageBuffer.Clear();
            }

            if (response.chats.Count == 0)
            {
                EmptyChatListBlock.Visibility = Visibility.Visible;
                return;
            }

            EmptyChatListBlock.Visibility = Visibility.Collapsed;

            // Сортировка чатов по времени последнего сообщения (новые выше)
            var sortedChats = response.chats
                .OrderByDescending(chat => chat.LastMessage?.SentAt.ToDateTime() ?? DateTime.MinValue)
                .ToList();

            foreach (var item in sortedChats)
            {
                if (item.IsGroupChat)
                {
                    App.ErideMessage.AddMessage($"Пропущен Gruppowy chat {item.Title}", new Erida { Type = MType.Debug });
                    continue;
                }

                // Определяем аватар
                string avatar = string.IsNullOrEmpty(item.Picture)
                    ? "pack://application:,,,/BarkFluff;component/Resources/Placeholders/userplaceholder.png"
                    : item.Picture;

                // Определяем статус чтения и заголовок
                var isRead = ChatItem.ReadingStatus.ForMe;
                var title = item.Title;
                var membersId = item.Members.Select(m => m.UserId).ToList();
                if (App.GParam.UserId == item.Members[0].UserId && App.GParam.UserId == item.Members[1].UserId)
                {
                    isRead = ChatItem.ReadingStatus.My;
                    title = "Избранное";
                    avatar = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/savedplaceholder.png";
                }

                membersId.Remove(App.GParam.UserId);
                long userId = membersId.FirstOrDefault(); // Возвращаем 0, если список пуст

                if (userId == 0)
                {
                    App.ErideMessage.AddMessage($"Ошибка: нет доступных userId для чата {item.Id}", new Erida { Type = MType.Error });
                    continue;
                }

                var messageItem = new ChatItem(
                    avatar,
                    title,
                    ChatItem.GetDisplayTextFromProto(item.LastMessage),
                    time: item.LastMessage?.SentAt.ToString() ?? string.Empty,
                    reading: isRead,
                    readBy: item.LastMessage?.ReadBy.ToList() ?? new List<long>(),
                    unReaded: item.CountUnread,
                    chatId: item.Id,
                    lastMessageId: item.LastMessage?.Id ?? 0,
                    firstUnreadId: item.FirstUnreadMessageId,
                    isGroupChat: item.IsGroupChat,
                    userId: userId
                );

                // Set the sender ID for the last message to enable proper read status display
                if (item.LastMessage != null)
                {
                    messageItem.TransferMessage = new MessageModel
                    {
                        MessageId = item.LastMessage.Id,
                        SenderId = item.LastMessage.SenderId,
                        ReadBy = item.LastMessage.ReadBy.ToList(),
                        Text = item.LastMessage.Content.Text,
                        SentAt = item.LastMessage.SentAt,
                        ChatId = item.Id,
                        Attachments = item.LastMessage.Content.Attachments?
                            .Select(a => new AttachmentsModel
                            {
                                Id = a.Id,
                                Type = a.Type,
                                FileId = a.FileId,
                                PreviewUrl = a.PreviewUrl,
                                Size = a.AttachmentSize
                            }).ToList() ?? new List<AttachmentsModel>()
                    };

                    // Инициализируем статус прочтения (галочки)
                    messageItem.UpdateMessage();

                    // Добавляем в буфер для быстрого поиска при обновлении статуса прочтения
                    lock (_chatBufferLock)
                    {
                        _chatLastMessageBuffer[item.Id] = item.LastMessage.Id;
                    }
                }

                ChatList.Children.Add(messageItem);
            }

            // Загружаем аватар текущего пользователя через кеш-сервис
            if (!string.IsNullOrEmpty(App.GParam.PictureUrl))
            {
                _currentUserAvatarFileId = FileCacheService.ExtractFileIdFromUrl(App.GParam.PictureUrl);
                var imagePath = App.FileCacheService.GetCachedFilePath(_currentUserAvatarFileId ?? string.Empty, FileType.Avatar, App.GParam.PictureUrl);
                SetTitleWindowAvatarImage(imagePath);
            }
        }
        public async void UserInfoUpdate()
        {
            var response = await App.ServerCommunication.GetUserData(App.GParam);

            App.GParam.UserId = response.Data.Id;
            App.GParam.UserName = response.Data.Username;
            App.GParam.FirstName = response.Data.FirstName;
            App.GParam.LastName = response.Data.LastName;
            App.GParam.Description = response.Data.Description;
            App.GParam.PictureUrl = response.Data.ProfilePictureUrl;
            App.GParam.RegistrationDate = response.Data.RegistrationDate;
            App.GParam.Email = response.Data.Email;
            //App.GParam.PictureId = response.Data.PictureId; // славик переделай //хз зачем оно нужно
            MainWindow.SaveSettings();


        }
        public void StartSlideDownAndFadeIn()
        {
            var storyboard = (Storyboard)this.Resources["SlideDownAndFadeIn"];
            storyboard.Begin();
        }

        public async void GetChatInfo(long userId)
        {
            var response = await App.ServerCommunication.GetUserData(App.GParam, userId);
            var avatar = string.Empty;
            if (!string.IsNullOrEmpty(response.Data.ProfilePictureUrl))
            {
                avatar = response.Data.ProfilePictureUrl;
            }
            if (!string.IsNullOrEmpty(response.Data.ProfilePicturePreviewUrl))
            {
                avatar = response.Data.ProfilePicturePreviewUrl;
            }
            else
            {
                avatar = FileCacheService.DefaultPlaceholder;
            }

            if (App.GParam.UserId == response.Data.Id)
            {

                ChatTitleUsername.Text = "Избранное";
                avatar = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/savedplaceholder.png";
                // Для избранного используем placeholder напрямую
                ChatAvatar.ImageSource = new BitmapImage(new Uri(avatar, UriKind.RelativeOrAbsolute));
            }
            else
            {
                ChatTitleUsername.Text = $"{response.Data.FirstName} {response.Data.LastName}";
                // Загружаем аватар через кеш-сервис
                _currentChatAvatarFileId = FileCacheService.ExtractFileIdFromUrl(avatar);
                var imagePath = App.FileCacheService.GetCachedFilePath(_currentChatAvatarFileId ?? string.Empty, FileType.Avatar, avatar);
                SetChatAvatarImage(imagePath);
            }

        }
        #endregion


        #region SearchBox

        private void SearchBoxFocus(object sender, RoutedEventArgs e)
        {

        }
        public void ChatListFadeIn()
        {
            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(500)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(opacityAnimation, ChatList);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacityAnimation);
            storyboard.Begin();
        }

        public void ChatListFadeOut()
        {
            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(opacityAnimation, ChatList);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacityAnimation);
            storyboard.Begin();
        }
        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            SearchTextBox.PlaceholderText = "Введите минимум 3 символа для поиска";
            SearchResultsHeader.Text = string.Empty;
            ExpandGrid();
        }

        private void ExpandGrid()
        {
            var expandAnimation = (Storyboard)FindResource("ExpandAnimation");
            expandAnimation.Begin();
            ChatListFadeOut();
        }

        private void CollapseGrid()
        {
            var collapseAnimation = (Storyboard)FindResource("CollapseAnimation");
            collapseAnimation.Begin();
            ChatListFadeIn();
        }

        public void ClearSearchAndHideResults()
        {
            SearchTextBox.Text = string.Empty;
            CollapseGrid();
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SearchTextBox.PlaceholderText = "Поиск";

            // потом заменить на что то другое а то так хуева оставлять
            ClearSearchAndHideResults();
        }
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTextBox.Text.Length >= 3)
            {

                var response = await App.ServerCommunication.SearchUser(App.GParam, SearchTextBox.Text);
                SearchCollection.Children.Clear();
                foreach (var item in response.userList)
                {
                    var a = new SearchElement(item);
                    SearchCollection.Children.Add(a);
                }
                SearchResultsHeader.Text = "Найдено " + response.userList.Count + " результатов";
            }
            else if (SearchTextBox.Text.Length <= 2 && SearchTextBox.Text.Length >= 1)
            {
                SearchCollection.Children.Clear();
                SearchTextBox.PlaceholderText = string.Empty;
                SearchResultsHeader.Text = "Введите минимум 3 символа для поиска";
            }
            else if (SearchTextBox.Text.Length == 0)
            {
                SearchCollection.Children.Clear();
                SearchResultsHeader.Text = string.Empty;
            }
        }

        #endregion

        #region боковая панель

        private bool isOpenPanel = false;
        private readonly CubicEase easingPanel = new CubicEase { EasingMode = EasingMode.EaseInOut };
        private void SidePanel_Loaded(object sender, RoutedEventArgs e)
        {
            SidePanel.Children.Clear();
            var sideBar = new SideBar();
            SidePanel.Children.Add(sideBar);
        }
        private void OpenPanelClick(object sender, RoutedEventArgs e)
        {
            if (!isOpenPanel)
                OpenPanel();
            else
                ClosePanel();
        }
        private void OverlayPanel_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClosePanel();
        }
        /// <summary>
        /// открыть Sidebar
        /// </summary>
        public void OpenPanel()
        {
            var anim = new ThicknessAnimation
            {
                From = new Thickness(-350, 0, 0, 0),
                To = new Thickness(0, 0, 0, 0),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingPanel
            };
            SidePanel.BeginAnimation(MarginProperty, anim);
            OverlayPanel.Visibility = Visibility.Visible;
            isOpenPanel = true;
        }
        /// <summary>
        /// Закрыть Sidebar
        /// </summary>
        public void ClosePanel()
        {
            var anim = new ThicknessAnimation
            {
                From = new Thickness(0, 0, 0, 0),
                To = new Thickness(-350, 0, 0, 0),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingPanel
            };
            SidePanel.BeginAnimation(MarginProperty, anim);
            OverlayPanel.Visibility = Visibility.Collapsed;
            isOpenPanel = false;
        }
        #endregion

        #region Центральный блок контента

        private bool isOpenCenter = false;
        private readonly CubicEase easingCenter = new CubicEase { EasingMode = EasingMode.EaseInOut };
        private Profile? _currentProfile = null;

        private void OpenCenterBlock(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var senderElement = sender as FrameworkElement;

            if (senderElement != null && !string.IsNullOrEmpty(senderElement.Tag?.ToString()))
            {
                var tag = senderElement.Tag.ToString();

                if (tag == "UserProfile")
                {
                    // Определяем, чей профиль открывать
                    if (senderElement.Name == "AvatarTitleWindowButton")
                    {
                        // Клик на аватар в заголовке - открываем свой профиль
                        ShowUserProfile(isCurrentUser: true);
                    }
                    else if (senderElement.Name == "ChatAvatarButton")
                    {
                        // Клик на аватар в чате - открываем профиль собеседника
                        if (ChatIdbyUserId.Value > 0)
                        {
                            ShowUserProfile(userId: ChatIdbyUserId.Value);
                        }
                    }

                    if (!isOpenCenter)
                    {
                        OpenCenterPanel();
                    }
                }
                else if (tag == "UpdateBlock")
                {
                    LaunchUpdater();
                }
            }
            else
            {
                if (!isOpenCenter)
                {
                    OpenCenterPanel();
                }
                else
                {
                    CloseCenterPanel();
                }
            }
        }

        /// <summary>
        /// Показывает профиль пользователя в центральной панели
        /// </summary>
        /// <param name="isCurrentUser">Если true, загружает профиль текущего пользователя</param>
        /// <param name="userId">ID пользователя для загрузки (если isCurrentUser = false)</param>
        private void ShowUserProfile(bool isCurrentUser = false, long userId = 0)
        {
            // Очищаем предыдущий контент
            CenterPanel.Child = null;

            // Создаем новый Profile контрол
            _currentProfile = new Profile();
            CenterPanel.Child = _currentProfile;

            if (isCurrentUser)
            {
                _currentProfile.LoadCurrentUserProfile();
            }
            else if (userId > 0)
            {
                _currentProfile.LoadUserProfile(userId);
            }
        }
        public void OpenSettings()
        {
            CenterPanel.Child = null;

            CenterPanel.Child = new BarkFluff.Client.WPF.UserControls.Settings();

            OpenCenterPanel();
        }
        public void OpenDebugMenu()
        {
            CenterPanel.Child = null;

            CenterPanel.Child = new BarkFluff.Client.WPF.UserControls.Debug.Menu();

            OpenCenterPanel();
        }

        public void OpenVideoEditor(string videoPath)
        {
            CenterPanel.Child = null;

            var editor = new BarkFluff.Client.WPF.UserControls.VideoEditor(videoPath);
            CenterPanel.Child = editor;

            OpenCenterPanel();
        }

        private void OpenCenterPanel()
        {
            CenterPanel.Visibility = Visibility.Visible;
            OverlayCenter.Visibility = Visibility.Visible;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingCenter
            };
            CenterPanel.BeginAnimation(OpacityProperty, anim);
            isOpenCenter = true;
        }

        private void CloseCenterPanel()
        {
            var anim = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingCenter
            };
            anim.Completed += (s, e) =>
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                OverlayCenter.Visibility = Visibility.Collapsed;
                // Очищаем контент при закрытии
                CenterPanel.Child = null;
                _currentProfile = null;
            };
            CenterPanel.BeginAnimation(OpacityProperty, anim);
            isOpenCenter = false;
        }
        private void OverlayCenter_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseCenterPanel();
        }


        #endregion

        #region обновление

        public async Task ProcessMessages(GlobalParam globalParam)
        {
            App.ErideMessage.AddMessage("Запуск процесса получения обновлений...", new Erida { Type = MType.Debug });

            // Подписываемся на события RealtimeUpdateService
            Services.App.RealtimeUpdateService.Instance.NewMessageReceived += OnNewMessageReceived;
            Services.App.RealtimeUpdateService.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;
            Services.App.RealtimeUpdateService.Instance.ReadReceiptReceived += OnReadReceiptReceived;

            // Start the realtime update service
            Services.App.RealtimeUpdateService.Instance.Start(globalParam);

            // Start global read receipt subscription (for chat list)
            Services.App.RealtimeUpdateService.Instance.StartGlobalReadReceiptSubscription(globalParam);

            // Start the online status service
            App.OnlineStatusService.Start(globalParam);
        }

        private void OnNewMessageReceived(string chatId, MessageModel message)
        {
            // Обновляем UI в основном потоке
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Проверяем, существует ли чат в списке чатов
                ChatItem? existingChatItem = null;
                foreach (var child in ChatList.Children)
                {
                    if (child is ChatItem chatItem && chatItem.ChatId == chatId)
                    {
                        existingChatItem = chatItem;
                        break;
                    }
                }

                if (existingChatItem != null)
                {
                    // Обновляем существующий чат
                    existingChatItem.TransferMessage = message;
                    existingChatItem.UpdateMessage();

                    // Увеличиваем счётчик непрочитанных, если сообщение не от текущего пользователя и чат не открыт
                    if (message.SenderId != App.GParam.UserId && ChatId.Value != chatId)
                    {
                        existingChatItem.IncrementUnreadCount();
                        // Устанавливаем FirstUnreadId если он равен 0
                        existingChatItem.SetFirstUnreadIdIfZero(message.MessageId);
                    }

                    // Показываем уведомление если сообщение не от текущего пользователя
                    if (message.SenderId != App.GParam.UserId)
                    {
                        _ = ShowNotificationForMessage(chatId, message, existingChatItem);
                    }
                }
                else
                {
                    // Новый чат - нужно добавить в список
                    AddNewChatToList(chatId, message);
                }

                // Обновляем список чатов новым сообщением
                UpdateChatWithMessage(message);

                // Если это сообщение для открытого чата, добавляем его в область сообщений
                if (!string.IsNullOrEmpty(ChatId.Value) && chatId == ChatId.Value)
                {
                    // Проверяем, есть ли у нас уже это сообщение (например, наше отправленное сообщение)
                    bool messageExists = false;
                    MessageBubble? existingBubble = null;

                    foreach (var child in MessageArea.Children)
                    {
                        if (child is MessageBubble bubble && bubble.MessageId == message.MessageId.ToString())
                        {
                            messageExists = true;
                            existingBubble = bubble;
                            break;
                        }
                    }

                    if (messageExists && existingBubble != null)
                    {
                        // Обновляем существующее сообщение (например, изменился список ReadBy)
                        existingBubble.UpdateReadByList(message.ReadBy);
                    }
                    else
                    {
                        // Добавляем разделитель даты при необходимости перед добавлением сообщения
                        AddDateSeparatorIfNeeded(message.SentAt.ToDateTime());

                        // Определяем владельца сообщения
                        var owner = message.SenderId == App.GParam.UserId
                            ? MessageBubble.MessageOwner.Me
                            : MessageBubble.MessageOwner.Interlocutor;
                        var type = GetMessageType(message);
                        var messageItem = new MessageBubble(owner, type, message, IsGroup);
                        AddMessage(messageItem);

                        // Автоматически отмечаем как прочитанное, если это входящее сообщение и чат открыт
                        if (message.SenderId != App.GParam.UserId)
                        {
                            MarkVisibleMessagesAsRead();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Показывает уведомление для нового сообщения
        /// </summary>
        private async Task ShowNotificationForMessage(string chatId, MessageModel message, ChatItem? chatItem)
        {
            try
            {
                // Получаем имя отправителя и аватар из ChatItem
                string senderName = chatItem?.ChatTitle ?? "Новое сообщение";
                string? avatarFileId = null;
                string? avatarUrl = null;

                // Пытаемся получить аватар - используем уже извлечённый fileId из ChatItem
                if (chatItem != null)
                {
                    avatarUrl = chatItem.AvatarUrl;
                    avatarFileId = chatItem.AvatarFileId;
                }

                // Показываем уведомление через NotificationManager
                await App.NotificationManager.ShowMessageNotificationAsync(
                    message,
                    senderName,
                    avatarFileId,
                    avatarUrl);
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка показа уведомления: {ex.Message}", new Erida { Type = MType.Error });
            }
        }

        /// <summary>
        /// Adds a new chat to the chat list when first message arrives
        /// </summary>
        private async void AddNewChatToList(string chatId, MessageModel message)
        {
            try
            {
                // Получаем полную информацию о чате с сервера
                var response = await App.ServerCommunication.GetChats(App.GParam);
                if (response.error.IsSuccess && response.chats != null)
                {
                    var newChat = response.chats.FirstOrDefault(c => c.Id == chatId);
                    if (newChat != null)
                    {
                        // Determine avatar and title
                        string avatar = string.IsNullOrEmpty(newChat.Picture)
                            ? "pack://application:,,,/BarkFluff;component/Resources/Placeholders/userplaceholder.png"
                            : newChat.Picture;

                        var title = newChat.Title;
                        var membersId = newChat.Members.Select(m => m.UserId).ToList();
                        membersId.Remove(App.GParam.UserId);
                        long userId = membersId.FirstOrDefault();

                        if (userId == 0)
                        {
                            // Try to get userId from message sender
                            userId = message.SenderId != App.GParam.UserId ? message.SenderId : 0;
                        }

                        var messageItem = new ChatItem(
                            avatar,
                            title,
                            ChatItem.GetDisplayText(message),
                            time: message.SentAt.ToString(),
                            reading: ChatItem.ReadingStatus.ForMe,
                            readBy: message.ReadBy,
                            unReaded: message.SenderId != App.GParam.UserId ? 1 : 0,
                            chatId: chatId,
                            lastMessageId: message.MessageId,
                            firstUnreadId: newChat.FirstUnreadMessageId,
                            isGroupChat: newChat.IsGroupChat,
                            userId: userId
                        );

                        messageItem.TransferMessage = message;
                        messageItem.UpdateMessage();

                        // Добавляем в буфер для отслеживания последнего сообщения
                        lock (_chatBufferLock)
                        {
                            _chatLastMessageBuffer[chatId] = message.MessageId;
                        }

                        // Вставляем в начало списка (самые свежие сверху)
                        ChatList.Children.Insert(0, messageItem);

                        // Показываем список чатов, если он был пуст
                        if (EmptyChatListBlock.Visibility == Visibility.Visible)
                        {
                            EmptyChatListBlock.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка добавления нового чата: {ex.Message}", new Erida { Type = MType.Error });
            }
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Update UI on the main thread
                if (isConnected)
                {
                    App.ErideMessage.AddMessage("Подключено к потоку обновлений", new Erida { Type = MType.Debug });
                    ErrorConnection.Visibility = Visibility.Collapsed;
                }
                else
                {
                    App.ErideMessage.AddMessage("Отключено от потока обновлений. Попытка переподключения...", new Erida { Type = MType.Warning });
                    ErrorConnection.Visibility = Visibility.Visible;
                }
            });
        }

        private void CleanupRealtimeService()
        {
            Services.App.RealtimeUpdateService.Instance.NewMessageReceived -= OnNewMessageReceived;
            Services.App.RealtimeUpdateService.Instance.ConnectionStatusChanged -= OnConnectionStatusChanged;
            Services.App.RealtimeUpdateService.Instance.ReadReceiptReceived -= OnReadReceiptReceived;
            // Глобальная подписка остается активной - она управляется самим RealtimeUpdateService
        }

        private void OnReadReceiptReceived(BarkFluff.Proto.Updates.MessageReadEvent update)
        {
            // Update UI on the main thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var chatId = update.ChatId;
                    var messageId = update.MessageId;
                    var newReadBy = update.NewReadBy.ToList();

                    // Быстрая проверка через буфер - нужно ли обновлять галочки в списке чатов
                    bool shouldUpdateChatList = false;
                    lock (_chatBufferLock)
                    {
                        if (_chatLastMessageBuffer.TryGetValue(chatId, out long lastMessageId))
                        {
                            shouldUpdateChatList = (lastMessageId == messageId);
                        }
                    }

                    // Update message bubble in the currently open chat
                    if (!string.IsNullOrEmpty(ChatId.Value) && ChatId.Value == chatId)
                    {
                        foreach (var child in MessageArea.Children)
                        {
                            if (child is MessageBubble bubble && bubble.MessageId == messageId.ToString())
                            {
                                bubble.UpdateReadByList(newReadBy);
                                break;
                            }
                        }
                    }

                    // Update ChatItem in chat list only if this is the last message
                    if (shouldUpdateChatList)
                    {
                        foreach (var child in ChatList.Children)
                        {
                            if (child is ChatItem chatItem && chatItem.ChatId == chatId)
                            {
                                chatItem.UpdateLastMessageReadStatus(messageId, newReadBy);
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.ErideMessage.AddMessage($"Ошибка обработки уведомления о прочтении: {ex.Message}", new Erida { Type = MType.Error });
                }
            });
        }

        #endregion

        #region Чаты

        public async void OpenChatFromSearch(long userId, string chatTitle)
        {
            var responseChatId = await App.ServerCommunication.GetPersonChatId(App.GParam, userId);
            if (!responseChatId.error.IsSuccess)
            {
                App.ErideMessage.AddMessage($"Что-то пошло не так, {responseChatId.error.ErrorMessage}", new Erida { Type = MType.Warning });
                return;
            }
            var responseChatInfo = await App.ServerCommunication.GetChatInfo(App.GParam, responseChatId.chatId);
            if (!responseChatInfo.error.IsSuccess)
            {
                App.ErideMessage.AddMessage($"Что-то пошло не так, {responseChatId.error.ErrorMessage}", new Erida { Type = MType.Warning });
                return;
            }
            OpenChatById(responseChatId.chatId, responseChatInfo.lastMessageId, responseChatInfo.isGroup, userId, responseChatInfo.title, 0);
        }

        public void OpenChatById(string chatId, long lastMessageId, bool isGroupChat, long userId, string title, long firstUnreadId = 0)
        {

            IsOpenChatEmpty = false;
            IsOpenChat.Value = true;
            TitleChat = title;
            _openedLastMessageId = lastMessageId;
            _oldestLoadedMessageId = 0;
            _firstUnreadMessageId = firstUnreadId;
            _hasMoreHistory = true;
            ChatId.Value = chatId;
            IsGroup = isGroupChat;

            GetChatInfo(userId);
            ChatIdbyUserId.Dispose();
            ChatIdbyUserId = new ReactiveLong(userId);
            App.ErideMessage.AddMessage($"Открытие чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });

            // Обновление онлайн статуса для открытого чата
            _currentChatUserId = userId;
            _isCurrentChatGroup = isGroupChat;

            // Если это личный чат - получить и показать текущий статус
            if (!isGroupChat && userId > 0)
            {
                // Трекаем пользователя для реалтайм обновлений
                App.OnlineStatusService.TrackUser(userId);

                // КРИТИЧНО: Делаем отдельный запрос для немедленного получения статуса
                _ = FetchAndUpdateOnlineStatus(userId);
            }
            else if (isGroupChat)
            {
                // Для групповых чатов - очищаем статус
                if (UserOnlineTime != null)
                {
                    UserOnlineTime.Text = string.Empty;
                }
            }
        }

        private async void CloseChatButton(object sender, RoutedEventArgs e)
        {
            App.ErideMessage.AddMessage($"Закрытие чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });
            IsOpenChat.Value = false;
            _openedLastMessageId = 0;
            ChatId.Value = string.Empty;
            ChatIdbyUserId.Dispose();
            ChatIdbyUserId = new ReactiveLong(0);
        }

        /// <summary>
        /// Обработчик клика по уведомлению - открывает соответствующий чат
        /// </summary>
        private void OnNotificationClicked(NotificationData data)
        {
            if (string.IsNullOrEmpty(data.ChatId))
                return;

            // Ищем чат в списке чатов
            ChatItem? targetChatItem = null;
            foreach (var child in ChatList.Children)
            {
                if (child is ChatItem chatItem && chatItem.ChatId == data.ChatId)
                {
                    targetChatItem = chatItem;
                    break;
                }
            }

            if (targetChatItem != null)
            {
                // Открываем чат через существующий метод
                OpenChatById(
                    targetChatItem.ChatId,
                    targetChatItem.LastMessageId,
                    targetChatItem.IsGroupChat,
                    targetChatItem.UserId,
                    targetChatItem.ChatTitle,
                    targetChatItem.FirstUnreadId);
            }
            else
            {
                // Чат не найден в списке - обновляем список чатов
                App.ErideMessage.AddMessage($"Чат {data.ChatId} не найден в списке, обновляем...", new Erida { Type = MType.Debug });
                _ = ChatUpdate();
            }
        }

        #endregion

        #region Attachments

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Все файлы (*.*)|*.*",
                Title = "Выберите файл"
            };

            if (dialog.ShowDialog() == true)
            {
                ShowAttachmentPreview(dialog.FileNames.ToList());
            }
        }

        public void ShowAttachmentPreview(List<string> filePaths)
        {
            ShowAttachmentPreviewWithText(() =>
            {
                AttachmentPreview.AddAttachments(filePaths);
            });
        }

        /// <summary>
        /// Открывает чат по ID и сразу показывает превью вложений с файлами
        /// Используется при перетаскивании файлов на ChatItem
        /// </summary>
        public void OpenChatAndShowAttachments(string chatId, long lastMessageId, bool isGroupChat, long userId, string title, List<string> filePaths)
        {
            // Сначала открываем чат
            OpenChatById(chatId, lastMessageId, isGroupChat, userId, title, 0);

            // Затем показываем превью вложений
            // Используем BeginInvoke чтобы дать чату время открыться
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowAttachmentPreview(filePaths);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Показывает AttachmentPreviewOverlay и переносит текст из TextForMessage в MessageTextBox overlay
        /// </summary>
        /// <param name="attachmentAction">Действие для добавления вложений в preview</param>
        private void ShowAttachmentPreviewWithText(Action attachmentAction)
        {
            // Захватываем текущий текст до любых операций
            string currentText = TextForMessage.Text ?? string.Empty;

            // Выполняем действие добавления вложений
            attachmentAction();

            // Переносим текст в overlay
            AttachmentPreview.SetMessageText(currentText);

            // Очищаем исходное текстовое поле
            TextForMessage.Text = string.Empty;

            // Показываем overlay
            AttachmentOverlay.Visibility = Visibility.Visible;
        }

        private void AttachmentPreview_OnCancel(object? sender, EventArgs e)
        {
            AttachmentOverlay.Visibility = Visibility.Collapsed;
            AttachmentPreview.Clear();
        }

        private async void AttachmentPreview_OnSend(object? sender, UserControls.SendAttachmentsEventArgs e)
        {
            AttachmentOverlay.Visibility = Visibility.Collapsed;

            if (e.SendSeparately)
            {
                // Отправить каждый файл как отдельное сообщение
                // Отправлять текст только с первым вложением, чтобы избежать дубликатов
                for (int i = 0; i < e.Attachments.Count; i++)
                {
                    var textToSend = i == 0 ? e.MessageText : string.Empty;
                    await SendMessageWithAttachments(textToSend, new List<UserControls.AttachmentPreviewItem> { e.Attachments[i] });
                }
            }
            else
            {
                // Отправить все файлы в одном сообщении
                await SendMessageWithAttachments(e.MessageText, e.Attachments);
            }

            AttachmentPreview.Clear();
        }

        private async Task SendMessageWithAttachments(string text, List<UserControls.AttachmentPreviewItem> attachments)
        {
            try
            {
                // Определяем получателя
                string recipientId = "0";
                bool isUserId = false;
                if (IsOpenChatEmpty)
                {
                    recipientId = ChatIdbyUserId.Value.ToString();
                    isUserId = true;
                }
                else
                {
                    recipientId = ChatId.Value;
                    isUserId = false;
                }

                // Create pending message model for UI
                var pendingMessage = new MessageModel
                {
                    Text = text,
                    ChatId = ChatId.Value,
                    SenderId = App.GParam.UserId,
                    SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    Attachments = new List<AttachmentsModel>()
                };

                // Создаём временные модели вложений для превью
                foreach (var attachment in attachments)
                {
                    pendingMessage.Attachments.Add(new AttachmentsModel
                    {
                        Type = DetermineAttachmentType(attachment.FileType),
                        FileId = string.Empty, // Will be filled after upload
                        PreviewUrl = attachment.FilePath, // Use local path as preview
                        Size = new FileInfo(attachment.FilePath).Length
                    });
                }

                // Определяем тип сообщения по первому вложению
                var messageType = attachments.Count > 0 ? GetMessageTypeFromAttachment(attachments[0].FileType) : MessageBubble.MessageType.Text;

                // Добавляем разделитель даты при необходимости (ПЕРЕД добавлением сообщения)
                AddDateSeparatorIfNeeded(DateTime.Now);

                // Создаём пузырь сообщения в состоянии ожидания
                var messageControl = new MessageBubble(MessageBubble.MessageOwner.Me, messageType, pendingMessage, IsGroup);

                // Настраиваем элементы загружаемых вложений для отображения индивидуального прогресса
                var localFilePaths = attachments.Select(a => a.FilePath).ToList();
                messageControl.SetupUploadingAttachments(localFilePaths);

                // Немедленно добавляем в UI (показывает загружаемые файлы с прогрессом)
                AddMessage(messageControl);

                // Загружаем файлы и получаем их ID
                var fileIds = new List<string>();
                for (int i = 0; i < attachments.Count; i++)
                {
                    var attachment = attachments[i];

                    // Создаём прогресс-репортер для конкретного вложения
                    var progress = new Progress<double>(percent =>
                    {
                        // Обновляем прогресс конкретного вложения
                        messageControl.UpdateAttachmentProgress(i, percent);
                    });

                    var (error, fileId) = await App.ServerCommunication.UploadFileAsync(
                        App.GParam,
                        attachment.FilePath,
                        attachment.FileType,
                        progress);

                    if (!error.IsSuccess || string.IsNullOrEmpty(fileId))
                    {
                        messageControl.MarkAttachmentFailed(i, error.ErrorMessage ?? "Неизвестная ошибка");
                        App.ErideMessage.AddMessage(
                            $"Ошибка загрузки файла {attachment.FileName}: {error.ErrorMessage}",
                            new Erida { Type = MType.Error });
                        continue;
                    }

                    fileIds.Add(fileId);
                    messageControl.MarkAttachmentUploaded(i, fileId);

                    // Обновляем вложение реальным fileId
                    if (i < pendingMessage.Attachments.Count)
                    {
                        pendingMessage.Attachments[i].FileId = fileId;
                    }

                    // Clean up temp file if from clipboard
                    if (attachment.IsFromClipboard)
                    {
                        try
                        {
                            if (File.Exists(attachment.FilePath))
                                File.Delete(attachment.FilePath);
                        }
                        catch
                        {
                            // Ignore errors deleting temp files
                        }
                    }
                }

                if (fileIds.Count == 0)
                {
                    App.ErideMessage.AddMessage("Не удалось загрузить ни один файл", new Erida { Type = MType.Error });
                    return;
                }

                // Отправляем сообщение с загруженными ID файлов
                (bool, string) type = new(isUserId, recipientId);
                var letter = new ForwardingLetter { Text = text, FilesId = fileIds };
                var response = await App.ServerCommunication.SendMessage(App.GParam, type, letter);

                if (!response.error.IsSuccess)
                {
                    App.ErideMessage.AddMessage(
                        $"Ошибка отправки сообщения: {response.error.ErrorMessage}",
                        new Erida { Type = MType.Error });
                }
                else if (response.message != null)
                {
                    // Обновляем контрол сообщения реальным ID и отмечаем как отправленное
                    messageControl.MessageId = response.message.MessageId.ToString();

                    // Заменяем панель загрузки реальным содержимым
                    messageControl.ReplaceUploadingWithContent(response.message, messageType);

                    // Отмечаем как отправленное (меняет иконку часов на галочку)
                    messageControl.MarkAsSent();

                    // Сохраняем в кеш
                    App.CacheManager.SaveMessage(
                        response.message.ChatId,
                        TitleChat,
                        response.message,
                        MessageOperation.Added);

                    // Обновляем список чатов
                    UpdateChatWithMessage(response.message);
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка отправки сообщения с вложениями: {ex.Message}", new Erida { Type = MType.Error });
            }
        }

        private MessageAttachmentType DetermineAttachmentType(Proto.Files.UploadFileType fileType)
        {
            return fileType switch
            {
                Proto.Files.UploadFileType.MessageAttachmentImage => MessageAttachmentType.Image,
                Proto.Files.UploadFileType.MessageAttachmentVideo => MessageAttachmentType.Video,
                Proto.Files.UploadFileType.MessageAttachmentGif => MessageAttachmentType.Gif,
                Proto.Files.UploadFileType.MessageAttachmentDocument => MessageAttachmentType.Document,
                _ => MessageAttachmentType.Document
            };
        }

        private MessageBubble.MessageType GetMessageTypeFromAttachment(Proto.Files.UploadFileType fileType)
        {
            return fileType switch
            {
                Proto.Files.UploadFileType.MessageAttachmentImage => MessageBubble.MessageType.Image,
                Proto.Files.UploadFileType.MessageAttachmentVideo => MessageBubble.MessageType.Video,
                Proto.Files.UploadFileType.MessageAttachmentGif => MessageBubble.MessageType.Gif,
                Proto.Files.UploadFileType.MessageAttachmentDocument => MessageBubble.MessageType.Document,
                _ => MessageBubble.MessageType.Document
            };
        }

        private void AddDateSeparatorIfNeeded(DateTime newMessageLocalDate)
        {
            // Используем только локальную дату для сравнения
            var newDate = newMessageLocalDate.Date;

            // Check if we need to add a date separator
            if (MessageArea.Children.Count == 0)
            {
                // Если это первое сообщение, добавляем заголовок даты
                var dateHeader = GetDateHeader(newDate);
                var dateControl = new DateHeaderControl { Text = dateHeader };
                dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                dateControl.Margin = new Thickness(0, 10, 0, 10);
                MessageArea.Children.Add(dateControl);
                return;
            }

            // Проверяем, есть ли уже разделитель с такой же датой
            foreach (var child in MessageArea.Children)
            {
                if (child is DateHeaderControl existingHeader)
                {
                    var headerText = existingHeader.Text;
                    var expectedText = GetDateHeader(newDate);

                    // Если уже есть разделитель с нужной датой - не добавляем новый
                    if (headerText == expectedText)
                    {
                        return;
                    }
                }
            }

            // Get the last message in the area (skip if last item is already a date header)
            var lastChild = MessageArea.Children[MessageArea.Children.Count - 1];

            if (lastChild is DateHeaderControl)
            {
                // If last item is already a date header, don't add another
                return;
            }

            if (lastChild is MessageBubble lastBubble && lastBubble.SentAt != null)
            {
                // Конвертируем UTC время сообщения в локальное для корректного сравнения
                var lastMessageLocalDate = lastBubble.SentAt.ToDateTime().ToLocalTime().Date;

                // Add separator if dates differ
                if (lastMessageLocalDate != newDate)
                {
                    var dateHeader = GetDateHeader(newDate);
                    var dateControl = new DateHeaderControl { Text = dateHeader };
                    dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                    dateControl.Margin = new Thickness(0, 10, 0, 10);
                    MessageArea.Children.Add(dateControl);
                }
            }
        }

        private void OnTextForMessagePasteCommand(object sender, ExecutedRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== OnTextForMessagePasteCommand вызван ===");

            // Получаем данные из буфера обмена напрямую
            var clipboard = Clipboard.GetDataObject();
            if (clipboard == null)
            {
                System.Diagnostics.Debug.WriteLine("Буфер обмена пуст");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"FileDrop present: {clipboard.GetDataPresent(DataFormats.FileDrop)}");
            System.Diagnostics.Debug.WriteLine($"Bitmap present: {clipboard.GetDataPresent(DataFormats.Bitmap)}");

            // Выводим все доступные форматы
            var formats = clipboard.GetFormats();
            System.Diagnostics.Debug.WriteLine($"Доступные форматы: {string.Join(", ", formats)}");

            if (clipboard.GetDataPresent(DataFormats.FileDrop))
            {
                // Файлы, вставленные из Проводника
                System.Diagnostics.Debug.WriteLine("Обработка FileDrop");
                e.Handled = true; // Предотвращаем стандартную обработку
                var files = (string[])clipboard.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Найдено файлов: {files.Length}");
                    ShowAttachmentPreview(files.ToList());
                }
            }
            else if (clipboard.GetDataPresent(DataFormats.Bitmap))
            {
                // Изображение, вставленное из буфера обмена (скриншот)
                System.Diagnostics.Debug.WriteLine("Обработка Bitmap");
                e.Handled = true; // Предотвращаем стандартную обработку
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    System.Diagnostics.Debug.WriteLine("Изображение получено");
                    ShowAttachmentPreviewWithText(() =>
                    {
                        AttachmentPreview.AddImageFromClipboard(image);
                    });
                }
            }
            else if (clipboard.GetDataPresent(DataFormats.Text) || clipboard.GetDataPresent(DataFormats.UnicodeText))
            {
                // Обычный текст - выполняем вставку вручную
                System.Diagnostics.Debug.WriteLine("Обработка обычного текста - ручная вставка");
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    // Получаем текущую позицию каретки и выделение
                    var textBox = TextForMessage;
                    int selectionStart = textBox.SelectionStart;
                    int selectionLength = textBox.SelectionLength;
                    string currentText = textBox.Text ?? string.Empty;

                    // Вставляем текст с заменой выделенного фрагмента
                    string newText = currentText.Substring(0, selectionStart) + text + currentText.Substring(selectionStart + selectionLength);
                    textBox.Text = newText;

                    // Устанавливаем каретку после вставленного текста
                    textBox.SelectionStart = selectionStart + text.Length;
                    textBox.SelectionLength = 0;
                }
                e.Handled = true;
            }
        }

        #endregion

        #region Drag and Drop

        private void OpenedChat_DragEnter(object sender, DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== DragEnter ===");

            // Проверяем, содержит ли перетаскиваемый объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                System.Diagnostics.Debug.WriteLine("FileDrop detected in drag");
                e.Effects = DragDropEffects.Copy;
                DragDropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void OpenedChat_DragOver(object sender, DragEventArgs e)
        {
            // Проверяем, содержит ли перетаскиваемый объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void OpenedChat_DragLeave(object sender, DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== DragLeave ===");
            DragDropOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        private void OpenedChat_Drop(object sender, DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== Drop ===");

            // Скрываем визуальный индикатор
            DragDropOverlay.Visibility = Visibility.Collapsed;

            // Проверяем, содержит ли перетаскиваемый объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Dropped {files.Length} файлов");
                    ShowAttachmentPreview(files.ToList());
                }
            }

            e.Handled = true;
        }

        #endregion

        #region Управление иконкой обновления

        /// <summary>
        /// Показывает иконку обновления в заголовке приложения
        /// </summary>
        public void ShowUpdateIcon()
        {
            UpdateIconBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Скрывает иконку обновления в заголовке приложения
        /// </summary>
        public void HideUpdateIcon()
        {
            UpdateIconBorder.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Updater Launch

        /// <summary>
        /// Запускает программу обновления Barkfluff.Updater.CLI.exe
        /// </summary>
        private void LaunchUpdater()
        {
            try
            {
                // Получаем путь к директории текущей программы
                string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string updaterPath = Path.Combine(currentDirectory, "Barkfluff.Updater.CLI.exe");

                // Проверяем, существует ли файл
                if (!File.Exists(updaterPath))
                {
                    App.ErideMessage.AddMessage($"Файл обновления не найден: {updaterPath}", new Erida { Type = MType.Error });
                    MessageBox.Show("Программа обновления не найдена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Создаем процесс для запуска обновления
                var processInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    UseShellExecute = true,
                    WorkingDirectory = currentDirectory
                };

                Process.Start(processInfo);
                App.ErideMessage.AddMessage("Программа обновления запущена", new Erida { Type = MType.Debug });

                // Закрываем текущее приложение для применения обновления
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка при запуске программы обновления: {ex.Message}", new Erida { Type = MType.Error });
                MessageBox.Show($"Не удалось запустить программу обновления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Другие действия

        public void OpenQRModal()
        {
            OpenCenterPanel();
            CenterPanel.Child = new UserControls.ProfileShare(App.GParam.UserName);
        }

        public void OpenImageViewer(List<AttachmentsModel> attachments, int currentIndex)
        {
            // Фильтровать только изображения и GIF
            var imageAttachments = attachments
                .Where(a => a.Type == MessageAttachmentType.Image ||
                            a.Type == MessageAttachmentType.Gif)
                .ToList();

            if (imageAttachments.Count == 0) return;

            var adjustedIndex = Math.Min(currentIndex, imageAttachments.Count - 1);

            ImageViewer.Attachments = imageAttachments;
            ImageViewer.CurrentIndex = adjustedIndex;
            ImageViewer.IsOpen = true;
            ImageViewerOverlay.Visibility = Visibility.Visible;
            ImageViewer.Focus(); // Для обработки клавиатуры
        }

        private void OnImageViewerClosed(object sender, EventArgs e)
        {
            ImageViewerOverlay.Visibility = Visibility.Collapsed;
            ImageViewer.IsOpen = false;
        }

        public void OpenVideoPlayer(List<AttachmentsModel> attachments, int currentIndex)
        {
            // Фильтровать только видео
            var videoAttachments = attachments
                .Where(a => a.Type == MessageAttachmentType.Video)
                .ToList();

            if (videoAttachments.Count == 0) return;

            var adjustedIndex = Math.Min(currentIndex, videoAttachments.Count - 1);

            VideoPlayer.Attachments = videoAttachments;
            VideoPlayer.CurrentIndex = adjustedIndex;
            VideoPlayer.IsOpen = true;
            VideoPlayerOverlay.Visibility = Visibility.Visible;
            VideoPlayer.Focus();
        }

        private void OnVideoPlayerClosed(object sender, EventArgs e)
        {
            VideoPlayerOverlay.Visibility = Visibility.Collapsed;
            VideoPlayer.IsOpen = false;
            VideoPlayer.StopAndCleanup();
        }

        #endregion
    }

    public static class ScrollAnimationBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(ScrollAnimationBehavior),
                new UIPropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static void SetVerticalOffset(DependencyObject target, double value)
        {
            target.SetValue(VerticalOffsetProperty, value);
        }

        public static double GetVerticalOffset(DependencyObject target)
        {
            return (double)target.GetValue(VerticalOffsetProperty);
        }

        private static void OnVerticalOffsetChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }
    }
}
