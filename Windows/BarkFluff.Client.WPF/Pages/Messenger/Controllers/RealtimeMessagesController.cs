using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;
using RealtimeUpdateService = BarkFluff.Client.WPF.Services.App.RealtimeUpdateService;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за real-time обновления через <see cref="RealtimeUpdateService"/>:
    /// новые сообщения, read receipts, статус подключения. Подписки оформляются в
    /// <see cref="Start"/> и снимаются в <see cref="Cleanup"/>.
    /// </summary>
    public sealed class RealtimeMessagesController
    {
        private readonly MessengerPage _page;
        private readonly StackPanel _chatList;
        private readonly StackPanel _messageArea;
        private readonly FrameworkElement _errorConnection;
        private readonly ChatHistoryController _history;
        private readonly ChatListController _chatListCtrl;
        private readonly MessageReadController _read;
        private readonly NotificationController _notifications;

        public RealtimeMessagesController(
            MessengerPage page,
            StackPanel chatList,
            StackPanel messageArea,
            FrameworkElement errorConnection,
            ChatHistoryController history,
            ChatListController chatListCtrl,
            MessageReadController read,
            NotificationController notifications)
        {
            _page = page;
            _chatList = chatList;
            _messageArea = messageArea;
            _errorConnection = errorConnection;
            _history = history;
            _chatListCtrl = chatListCtrl;
            _read = read;
            _notifications = notifications;
        }

        /// <summary>
        /// Подписывается на события <see cref="RealtimeUpdateService"/> и запускает
        /// основной стрим обновлений и глобальную подписку read receipts.
        /// Вызывается из background потока (<c>Task.Run</c>) в <c>MessengerPage_Loaded</c>.
        /// </summary>
        public void Start(GlobalParam globalParam)
        {
            App.ErideMessage.AddMessage("Запуск процесса получения обновлений...", new Erida { Type = MType.Debug });

            RealtimeUpdateService.Instance.NewMessageReceived += OnNewMessageReceived;
            RealtimeUpdateService.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;
            RealtimeUpdateService.Instance.ReadReceiptReceived += OnReadReceiptReceived;

            RealtimeUpdateService.Instance.Start(globalParam);
            RealtimeUpdateService.Instance.StartGlobalReadReceiptSubscription(globalParam);

            App.OnlineStatusService.Start(globalParam);
        }

        /// <summary>
        /// Отписывается от всех подписок RealtimeUpdateService и останавливает
        /// глобальную подписку read receipts.
        /// Bugfix #2: раньше глобальная подписка не останавливалась при Unloaded,
        /// из-за чего оставался "висящий" сетевой Task. Теперь останавливается,
        /// а при повторном Loaded подписка запускается заново.
        /// </summary>
        public void Cleanup()
        {
            RealtimeUpdateService.Instance.NewMessageReceived -= OnNewMessageReceived;
            RealtimeUpdateService.Instance.ConnectionStatusChanged -= OnConnectionStatusChanged;
            RealtimeUpdateService.Instance.ReadReceiptReceived -= OnReadReceiptReceived;
            RealtimeUpdateService.Instance.StopGlobalReadReceiptSubscription();
        }

        private void OnNewMessageReceived(string chatId, MessageModel message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Проверяем, существует ли чат в списке чатов
                ChatItem? existingChatItem = null;
                foreach (var child in _chatList.Children)
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
                    if (message.SenderId != App.GParam.UserId && _page.ChatId.Value != chatId)
                    {
                        existingChatItem.IncrementUnreadCount();
                        existingChatItem.SetFirstUnreadIdIfZero(message.MessageId);
                    }

                    // Показываем уведомление если сообщение не от текущего пользователя
                    if (message.SenderId != App.GParam.UserId)
                    {
                        _ = _notifications.ShowNotificationForMessageAsync(chatId, message, existingChatItem);
                    }
                }
                else
                {
                    // Новый чат - нужно добавить в список
                    _ = AddNewChatToListAsync(chatId, message);
                }

                // Обновляем список чатов новым сообщением
                _chatListCtrl.UpdateChatWithMessage(message);

                // Если это сообщение для открытого чата, добавляем его в область сообщений
                if (!string.IsNullOrEmpty(_page.ChatId.Value) && chatId == _page.ChatId.Value)
                {
                    // Проверяем, есть ли у нас уже это сообщение (например, наше отправленное сообщение)
                    bool messageExists = false;
                    MessageBubble? existingBubble = null;
                    bool isPendingMatch = false;

                    foreach (var child in _messageArea.Children)
                    {
                        if (child is MessageBubble bubble && bubble.MessageId == message.MessageId.ToString())
                        {
                            messageExists = true;
                            existingBubble = bubble;
                            break;
                        }

                        // Проверка оптимистичного сообщения: пустой MessageId от текущего пользователя
                        if (child is MessageBubble pendingBubble
                            && string.IsNullOrEmpty(pendingBubble.MessageId)
                            && pendingBubble.SenderId == message.SenderId
                            && message.SenderId == App.GParam.UserId)
                        {
                            messageExists = true;
                            existingBubble = pendingBubble;
                            isPendingMatch = true;
                            break;
                        }
                    }

                    if (messageExists && existingBubble != null)
                    {
                        // Если это был оптимистичный пендинг - обновляем его полностью
                        if (isPendingMatch)
                        {
                            existingBubble.MessageId = message.MessageId.ToString();
                            existingBubble.MarkAsSent();
                        }

                        // Обновляем существующее сообщение (например, изменился список ReadBy)
                        existingBubble.UpdateReadByList(message.ReadBy);
                    }
                    else
                    {
                        // Добавляем разделитель даты при необходимости перед добавлением сообщения
                        _history.AddDateSeparatorIfNeeded(message.SentAt.ToDateTime());

                        // Определяем владельца сообщения
                        var owner = message.SenderId == App.GParam.UserId
                            ? MessageBubble.MessageOwner.Me
                            : MessageBubble.MessageOwner.Interlocutor;
                        var type = MessageTypeMapper.GetMessageType(message);
                        var messageItem = new MessageBubble(owner, type, message, _page.IsGroup);
                        _history.AddMessage(messageItem);

                        // Автоматически отмечаем как прочитанное, если это входящее сообщение и чат открыт
                        if (message.SenderId != App.GParam.UserId)
                        {
                            _read.MarkVisibleMessagesAsRead();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Добавляет в список чатов новый чат при получении первого сообщения.
        /// </summary>
        private async Task AddNewChatToListAsync(string chatId, MessageModel message)
        {
            try
            {
                // Получаем полную информацию о чате с сервера
                var chat = await App.ServerCommunication.GetChatInfo(App.GParam, chatId);
                if (chat.error.IsSuccess)
                {
                    if (chat.chatInfo.IsGroup)
                    {
                        MessageBox.Show("Групповые чаты не реализованны, пропускаем, для поиска по коду 73248334");
                        return;
                    }
                    if (chat.chatInfo.ChatId != string.Empty)
                    {
                        var title = chat.chatInfo.Title.Trim();

                        // Проверяем, является ли чат self-chat (Избранное)
                        bool isSelfChat = (chat.chatInfo.Members.Count >= 2 &&
                                           chat.chatInfo.Members[0] == App.GParam.UserId &&
                                           chat.chatInfo.Members[1] == App.GParam.UserId);

                        long userId;
                        string avatar;
                        if (isSelfChat)
                        {
                            avatar = "SavedChat";
                            title = "Избранное";
                            userId = App.GParam.UserId;
                        }
                        else
                        {
                            avatar = string.IsNullOrEmpty(chat.chatInfo.Picture)
                                ? "UserWithoutAvatar"
                                : chat.chatInfo.Picture;
                            userId = chat.chatInfo.Members.FirstOrDefault(m => m != App.GParam.UserId);

                            if (userId == 0)
                            {
                                userId = message.SenderId != App.GParam.UserId ? message.SenderId : 0;
                            }
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
                            firstUnreadId: chat.chatInfo.FirstUnreadId,
                            isGroupChat: chat.chatInfo.IsGroup,
                            userId: userId
                        );

                        messageItem.TransferMessage = message;
                        messageItem.UpdateMessage();

                        _chatListCtrl.SetLastMessageIdForChat(chatId, message.MessageId);

                        // Вставляем в начало списка (самые свежие сверху)
                        _chatList.Children.Insert(0, messageItem);
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
                if (isConnected)
                {
                    App.ErideMessage.AddMessage("Подключено к потоку обновлений", new Erida { Type = MType.Debug });
                    _errorConnection.Visibility = Visibility.Collapsed;
                }
                else
                {
                    App.ErideMessage.AddMessage("Отключено от потока обновлений. Попытка переподключения...", new Erida { Type = MType.Warning });
                    _errorConnection.Visibility = Visibility.Visible;
                }
            });
        }

        private void OnReadReceiptReceived(BarkFluff.Proto.Updates.MessageReadEvent update)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var chatId = update.ChatId;
                    var messageId = update.MessageId;
                    var newReadBy = update.NewReadBy.ToList();

                    // Быстрая проверка через буфер - нужно ли обновлять галочки в списке чатов
                    bool shouldUpdateChatList = false;
                    if (_chatListCtrl.TryGetLastMessageIdForChat(chatId, out long lastMessageId))
                    {
                        shouldUpdateChatList = (lastMessageId == messageId);
                    }

                    // Update message bubble in the currently open chat
                    if (!string.IsNullOrEmpty(_page.ChatId.Value) && _page.ChatId.Value == chatId)
                    {
                        foreach (var child in _messageArea.Children)
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
                        foreach (var child in _chatList.Children)
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
    }
}
