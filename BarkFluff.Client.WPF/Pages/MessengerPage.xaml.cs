using BarkFluff.Client.WPF.Reactive;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.Services.Notification;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

            // Подписываемся на событие вставки в TextForMessage
            DataObject.AddPastingHandler(TextForMessage, OnTextForMessagePaste);
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
                DisplayMessages(cachedMessages);
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

                DisplayMessages(response.messages);
            }

            // Глобальная подписка на уведомления о прочтении уже запущена в ProcessMessages
        }

        private void DisplayMessages(List<MessageModel> messages)
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

            foreach (var group in groupedMessages)
            {
                // Добавляем контрол с датой по центру
                var dateHeader = GetDateHeader(group.Key);
                var dateControl = new DateHeaderControl { Text = dateHeader };
                dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                dateControl.Margin = new Thickness(0, 10, 0, 10);
                MessageArea.Children.Add(dateControl);

                // Добавляем сообщения группы
                foreach (var item in group)
                {
                    var owner = item.SenderId == App.GParam.UserId ? MessageBubble.MessageOwner.Me : MessageBubble.MessageOwner.Interlocutor;
                    var type = GetMessageType(item);
                    var messageItem = new MessageBubble(owner, type, item, IsGroup);
                    AddMessage(messageItem);
                }
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

            TitleWindow.Text = "Barkfluff";

            UserInfoUpdate();
            ChatUpdate();
            await Task.Run(() => ProcessMessages(App.GParam));

            // Выполнение задачи от протокола

            App.MessagerTask.PropertyChanged += MessagerTask_PropertyChanged;

            var task = App.MessagerTask.Value;


            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenChatViaProtocol(task);
            }
        }

        private async void MessagerTask_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var task = App.MessagerTask.Value;
            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenChatViaProtocol(task);
            }
            else
            {
                return;
            }




        }
        #endregion

        #region Обработчка аргументов из протокола

        private async void OpenChatViaProtocol(string task)
        {
            try
            {
                var temp1 = task.Split("//");
                var temp2 = temp1[1].Replace("/", "");
                var command = temp2.Split("=")[0];
                var arg = temp2.Split("=")[1];
                if (command == "user-username")
                {
                    var result = await App.ServerCommunication.SearchUser(App.GParam, arg);
                    if (result.userList.Count > 0)
                    {
                        var user = result.userList[0];
                        IsOpenChatEmpty = true;
                        IsOpenChat.Value = true;
                        ChatIdbyUserId.Value = user.Id;
                    }
                    else
                    {
                        MessageBox.Show("Пользователь не найден");
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

            // Отметить видимые сообщения как прочитанные с небольшой задержкой
            System.Windows.Threading.DispatcherTimer delayTimer = new System.Windows.Threading.DispatcherTimer();
            delayTimer.Interval = TimeSpan.FromMilliseconds(INITIAL_MARK_DELAY_MS);
            delayTimer.Tick += (s, args) =>
            {
                delayTimer.Stop();
                MarkVisibleMessagesAsRead();
            };
            delayTimer.Start();
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

            // Сортировка ChatItem по времени последнего сообщения (новые сверху)
            var sortedChatItems = ChatList.Children.OfType<ChatItem>()
                .OrderByDescending(chatItem => chatItem.TransferMessage?.SentAt.ToDateTime() ?? DateTime.MinValue)
                .ToList();

            // Очищаем и перестраиваем ChatList
            ChatList.Children.Clear();
            foreach (var chatItem in sortedChatItems)
            {
                ChatList.Children.Add(chatItem);
            }
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
                    App.ErideMessage.AddMessage($"Пропущен групповой чат {item.Title}", new Erida { Type = MType.Debug });
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
                            isGroupChat: newChat.IsGroupChat,
                            userId: userId
                        );

                        messageItem.TransferMessage = message;

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
                // Обновляем UI в основном потоке
                if (isConnected)
                {
                    App.ErideMessage.AddMessage("Подключено к потоку обновлений", new Erida { Type = MType.Debug });
                }
                else
                {
                    App.ErideMessage.AddMessage("Отключено от потока обновлений", new Erida { Type = MType.Warning });
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

        public void OpenChatById(string chatId, long lastMessageId, bool isGroupChat, long userId, string title)
        {

            IsOpenChatEmpty = false;
            IsOpenChat.Value = true;
            TitleChat = title;
            _openedLastMessageId = lastMessageId;
            _oldestLoadedMessageId = 0;
            _hasMoreHistory = true;
            ChatId.Value = chatId;
            IsGroup = isGroupChat;

            GetChatInfo(userId);
            ChatIdbyUserId.Dispose();
            ChatIdbyUserId = new ReactiveLong(userId);
            App.ErideMessage.AddMessage($"Открытие чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });
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
                    targetChatItem.ChatTitle);
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
            AttachmentMenuPopup.IsOpen = !AttachmentMenuPopup.IsOpen;
        }

        private void AttachMediaButton_Click(object sender, RoutedEventArgs e)
        {
            AttachmentMenuPopup.IsOpen = false;
            OpenFileDialog("фото или видео");
        }

        private void AttachDocumentButton_Click(object sender, RoutedEventArgs e)
        {
            AttachmentMenuPopup.IsOpen = false;
            OpenFileDialog("файл");
        }

        private void OpenFileDialog(string fileType)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true
            };

            if (fileType == "фото или видео")
            {
                dialog.Filter = "Изображения и видео|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.mp4;*.avi;*.mov;*.mkv;*.webm|Все файлы (*.*)|*.*";
                dialog.Title = "Выберите фото или видео";
            }
            else
            {
                dialog.Filter = "Все файлы (*.*)|*.*";
                dialog.Title = "Выберите файл";
            }

            if (dialog.ShowDialog() == true)
            {
                ShowAttachmentPreview(dialog.FileNames.ToList());
            }
        }

        private void ShowAttachmentPreview(List<string> filePaths)
        {
            AttachmentPreview.AddAttachments(filePaths);
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

        private void OnTextForMessagePaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.FileDrop))
            {
                // Файлы, вставленные из Проводника
                e.CancelCommand();
                var files = (string[])e.DataObject.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    ShowAttachmentPreview(files.ToList());
                }
            }
            else if (e.DataObject.GetDataPresent(DataFormats.Bitmap))
            {
                // Изображение, вставленное из буфера обмена (скриншот)
                e.CancelCommand();
                var image = (BitmapSource)e.DataObject.GetData(DataFormats.Bitmap);
                if (image != null)
                {
                    AttachmentPreview.AddImageFromClipboard(image);
                    AttachmentOverlay.Visibility = Visibility.Visible;
                }
            }
        }

        #endregion

        #region Другие действия

        public void OpenQRModal()
        {
            OpenCenterPanel();
            CenterPanel.Child = new UserControls.ProfileShare(App.GParam.UserName);
        }

        #endregion
    }
}
