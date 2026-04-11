using BarkFluff.Client.WPF.Reactive;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls;

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за открытие/закрытие чатов, заголовок и аватар выбранного чата,
    /// а также за подписки на реактивные свойства <see cref="MessengerPage.IsOpenChat"/>,
    /// <see cref="MessengerPage.ChatId"/>, <see cref="MessengerPage.ChatIdbyUserId"/>.
    /// </summary>
    public sealed class ChatOpeningController
    {
        private readonly MessengerPage _page;
        private readonly StackPanel _messageArea;
        private readonly StackPanel _chatList;
        private readonly TextBlock _chatTitleUsername;
        private readonly CachedAvatar _chatAvatarButton;
        private readonly FrameworkElement _openedChat;
        private readonly FrameworkElement _noDialogMessage;
        private readonly ChatHistoryController _history;
        private readonly OnlineStatusController _onlineStatus;

        // Текущий подписанный экземпляр ChatIdbyUserId — необходим, потому что
        // ReactiveLong пересоздаётся в OpenChatById/CloseChat и подписки теряются.
        private ReactiveLong? _subscribedChatIdbyUserId;

        public string? CurrentChatAvatarFileId { get; private set; }

        public ChatOpeningController(
            MessengerPage page,
            StackPanel messageArea,
            StackPanel chatList,
            TextBlock chatTitleUsername,
            CachedAvatar chatAvatarButton,
            FrameworkElement openedChat,
            FrameworkElement noDialogMessage,
            ChatHistoryController history,
            OnlineStatusController onlineStatus)
        {
            _page = page;
            _messageArea = messageArea;
            _chatList = chatList;
            _chatTitleUsername = chatTitleUsername;
            _chatAvatarButton = chatAvatarButton;
            _openedChat = openedChat;
            _noDialogMessage = noDialogMessage;
            _history = history;
            _onlineStatus = onlineStatus;
        }

        /// <summary>
        /// Подписывает обработчики на реактивные свойства страницы.
        /// Должен вызываться единожды из конструктора <see cref="MessengerPage"/>.
        /// </summary>
        public void Attach()
        {
            _page.IsOpenChat.PropertyChanged += OnIsOpenChatChanged;
            _page.ChatId.PropertyChanged += OnChatIdChanged;
            RebindChatIdbyUserId();
        }

        public void Detach()
        {
            _page.IsOpenChat.PropertyChanged -= OnIsOpenChatChanged;
            _page.ChatId.PropertyChanged -= OnChatIdChanged;
            if (_subscribedChatIdbyUserId != null)
            {
                _subscribedChatIdbyUserId.PropertyChanged -= OnChatIdbyUserIdChanged;
                _subscribedChatIdbyUserId = null;
            }
        }

        /// <summary>
        /// Bugfix: страница пересоздаёт <see cref="MessengerPage.ChatIdbyUserId"/> в
        /// <see cref="OpenChatById"/>/<see cref="CloseChat"/>. Раньше после такого пересоздания
        /// подписка терялась и обработчик больше не вызывался. Теперь после каждого
        /// пересоздания контроллер переподписывает обработчик на новый экземпляр.
        /// </summary>
        private void RebindChatIdbyUserId()
        {
            if (ReferenceEquals(_subscribedChatIdbyUserId, _page.ChatIdbyUserId))
                return;

            if (_subscribedChatIdbyUserId != null)
            {
                _subscribedChatIdbyUserId.PropertyChanged -= OnChatIdbyUserIdChanged;
            }

            _subscribedChatIdbyUserId = _page.ChatIdbyUserId;
            _subscribedChatIdbyUserId.PropertyChanged += OnChatIdbyUserIdChanged;
        }

        private async void OnChatIdbyUserIdChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_page.ChatIdbyUserId.Value == 0) { return; } // если chatId пустой, выходим
            _page.IsOpenChat.Value = true;
            _page.IsOpenChatEmpty = true;
            _page.IsGroup = false;
            _page.ChatId.Value = string.Empty;
            App.ErideMessage.AddMessage($"Открытие чата с UserID: {_page.ChatIdbyUserId.Value}", new Erida { Type = MType.Debug });
            _history.ResetForNewChat(0, 0);
            await GetChatInfoAsync(_page.ChatIdbyUserId.Value);

            // Обновление онлайн статуса для открытого чата (личный чат)
            _onlineStatus.SetCurrentChat(_page.ChatIdbyUserId.Value, false);
        }

        private async void OnChatIdChanged(object? sender, PropertyChangedEventArgs e)
        {
            _messageArea.Children.Clear();
            GC.Collect();

            if (string.IsNullOrEmpty(_page.ChatId.Value) || _page.IsOpenChatEmpty || _history.OpenedLastMessageId == 0)
            {
                return;
            }
            _page.ChatIdbyUserId.Value = 0;
            App.ErideMessage.AddMessage($"Загрузка сообщений чата с ID: {_page.ChatId.Value}", new Erida { Type = MType.Debug });

            // Сначала показываем кешированные сообщения
            var cachedMessages = App.CacheManager.GetMessages(_page.ChatId.Value, _history.OpenedLastMessageId, 50);
            if (cachedMessages != null && cachedMessages.Count > 0)
            {
                App.ErideMessage.AddMessage($"Показываем {cachedMessages.Count} кешированных сообщений", new Erida { Type = MType.Debug });
                _history.DisplayMessages(cachedMessages, _history.FirstUnreadMessageId != 0);
            }

            // Затем загружаем актуальные сообщения с сервера
            var response = await App.ServerCommunication.GetMessages(App.GParam, _page.ChatId.Value, _history.OpenedLastMessageId);
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
                    App.CacheManager.SaveMessage(_page.ChatId.Value, _page.TitleChat, msg, MessageOperation.Added);
                }

                _history.DisplayMessages(response.messages, _history.FirstUnreadMessageId != 0);
            }
        }

        private void OnIsOpenChatChanged(object? sender, PropertyChangedEventArgs e)
        {
            _openedChat.Visibility = _page.IsOpenChat.Value ? Visibility.Visible : Visibility.Collapsed;
        }

        public async Task OpenChatFromSearchAsync(long userId)
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
            OpenChatById(responseChatId.chatId, responseChatInfo.chatInfo.LastMessageId, responseChatInfo.chatInfo.IsGroup, userId, responseChatInfo.chatInfo.Title, 0);
        }

        public void OpenChatById(string chatId, long lastMessageId, bool isGroupChat, long userId, string title, long firstUnreadId = 0)
        {
            // Очищаем уведомления этого чата из Action Center при открытии
            App.NotificationManager.ClearNotificationsForChat(chatId);

            _noDialogMessage.Visibility = Visibility.Collapsed;

            _page.IsOpenChatEmpty = false;
            _page.IsOpenChat.Value = true;
            _page.TitleChat = title;
            _history.ResetForNewChat(lastMessageId, firstUnreadId);
            _page.ChatId.Value = chatId;
            _page.IsGroup = isGroupChat;

            // Сбрасываем бейдж непрочитанных сообщений при открытии чата
            foreach (var child in _chatList.Children)
            {
                if (child is ChatItem chatItem && chatItem.ChatId == chatId)
                {
                    chatItem.ResetUnreadCount();
                    chatItem.ResetFirstUnreadId();
                    break;
                }
            }

            _ = GetChatInfoAsync(userId);
            _page.ChatIdbyUserId.Dispose();
            _page.ChatIdbyUserId = new ReactiveLong(userId);
            RebindChatIdbyUserId();
            App.ErideMessage.AddMessage($"Открытие чата с ID: {_page.ChatId.Value}", new Erida { Type = MType.Debug });

            // Обновление онлайн статуса для открытого чата
            _onlineStatus.SetCurrentChat(userId, isGroupChat);

            // Если это личный чат - делаем отдельный запрос для немедленного получения статуса
            if (!isGroupChat && userId > 0)
            {
                _ = _onlineStatus.FetchAndUpdateOnlineStatusAsync(userId);
            }
        }

        public void CloseChat()
        {
            App.ErideMessage.AddMessage($"Закрытие чата с ID: {_page.ChatId.Value}", new Erida { Type = MType.Debug });
            _page.IsOpenChat.Value = false;
            _history.Clear();
            _page.ChatId.Value = string.Empty;
            _page.ChatIdbyUserId.Dispose();
            _page.ChatIdbyUserId = new ReactiveLong(0);
            RebindChatIdbyUserId();
            _noDialogMessage.Visibility = Visibility.Visible;
        }

        public async Task GetChatInfoAsync(long userId)
        {
            var response = await App.ServerCommunication.GetUserData(App.GParam, userId);
            var avatar = string.Empty;
            if (!string.IsNullOrEmpty(response.Data.ProfilePicturePreviewUrl))
            {
                avatar = response.Data.ProfilePicturePreviewUrl;
            }
            else if (!string.IsNullOrEmpty(response.Data.ProfilePictureUrl))
            {
                avatar = response.Data.ProfilePictureUrl;
            }

            if (App.GParam.UserId == response.Data.Id)
            {
                _chatTitleUsername.Text = "Избранное";
                _chatAvatarButton.FileId = null;
                _chatAvatarButton.FileUrl = null;
                _chatAvatarButton.AvatarType = AvatarType.SavedChat;
            }
            else
            {
                _chatTitleUsername.Text = $"{response.Data.FirstName} {response.Data.LastName}";
                // Загружаем аватар через кеш-сервис
                CurrentChatAvatarFileId = FileCacheService.ExtractFileIdFromUrl(avatar);
                if (string.IsNullOrEmpty(CurrentChatAvatarFileId))
                {
                    _chatAvatarButton.FileId = null;
                    _chatAvatarButton.FileUrl = null;
                    _chatAvatarButton.AvatarType = AvatarType.UserWithoutAvatar;
                    return;
                }
                // Сбрасываем тип чтобы не вызвать LoadImage преждевременно
                _chatAvatarButton.AvatarType = AvatarType.UserWithoutAvatar;
                // Устанавливаем URL и FileId пока тип UserWithoutAvatar (LoadImage вернётся сразу)
                _chatAvatarButton.FileUrl = avatar;
                _chatAvatarButton.FileId = CurrentChatAvatarFileId;
                // Переключаем тип — это запустит LoadImage с FileId и FileUrl
                _chatAvatarButton.AvatarType = AvatarType.Image;
            }
        }
    }
}
