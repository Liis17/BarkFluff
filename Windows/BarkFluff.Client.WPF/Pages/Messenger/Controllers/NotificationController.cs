using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.Services.Notification;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System;
using System.Threading.Tasks;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за интеграцию уведомлений (WinRT toast) со страницей мессенджера:
    /// показ уведомлений о новых сообщениях, клики по уведомлениям
    /// (с открытием чата через <see cref="ChatOpeningController"/>) и обновление аватарок
    /// в заголовке/списке при событии <see cref="FileCacheService.FileCached"/>.
    /// </summary>
    public sealed class NotificationController
    {
        private readonly MessengerPage _page;
        private readonly CachedAvatar _chatAvatarButton;
        private readonly CachedAvatar _avatarTitleWindowButton;
        private readonly ChatListController _chatListCtrl;
        private readonly ChatOpeningController _openingCtrl;

        public NotificationController(
            MessengerPage page,
            CachedAvatar chatAvatarButton,
            CachedAvatar avatarTitleWindowButton,
            ChatListController chatListCtrl,
            ChatOpeningController openingCtrl)
        {
            _page = page;
            _chatAvatarButton = chatAvatarButton;
            _avatarTitleWindowButton = avatarTitleWindowButton;
            _chatListCtrl = chatListCtrl;
            _openingCtrl = openingCtrl;
        }

        public void Attach()
        {
            App.FileCacheService.FileCached += OnFileCached;
            App.NotificationManager.NotificationClicked += OnNotificationClicked;
        }

        public void Detach()
        {
            App.FileCacheService.FileCached -= OnFileCached;
            App.NotificationManager.NotificationClicked -= OnNotificationClicked;
        }

        /// <summary>
        /// Показывает WinRT-уведомление о новом сообщении. Имя отправителя и аватар
        /// берутся из <see cref="ChatItem"/>, если он передан (чат уже был в списке).
        /// </summary>
        public async Task ShowNotificationForMessageAsync(string chatId, MessageModel message, ChatItem? chatItem)
        {
            try
            {
                string senderName = chatItem?.ChatTitle ?? "Новое сообщение";
                string? avatarFileId = null;
                string? avatarUrl = null;

                if (chatItem != null)
                {
                    avatarUrl = chatItem.AvatarUrl;
                    avatarFileId = chatItem.AvatarFileId;
                }

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

        private void OnFileCached(string fileId, string filePath, FileType fileType)
        {
            if (fileType != FileType.Avatar) return;

            _page.Dispatcher.Invoke(() =>
            {
                if (fileId == _openingCtrl.CurrentChatAvatarFileId)
                {
                    _chatAvatarButton.Refresh();
                }
                if (fileId == _chatListCtrl.CurrentUserAvatarFileId)
                {
                    _avatarTitleWindowButton.Refresh();
                }
            });
        }

        /// <summary>
        /// Обработчик клика по уведомлению — открывает соответствующий чат.
        /// Если чат не найден в списке, запускает обновление списка чатов.
        /// </summary>
        private void OnNotificationClicked(NotificationData data)
        {
            if (string.IsNullOrEmpty(data.ChatId))
                return;

            ChatItem? targetChatItem = null;
            foreach (var child in _page.ChatList.Children)
            {
                if (child is ChatItem chatItem && chatItem.ChatId == data.ChatId)
                {
                    targetChatItem = chatItem;
                    break;
                }
            }

            if (targetChatItem != null)
            {
                _openingCtrl.OpenChatById(
                    targetChatItem.ChatId,
                    targetChatItem.LastMessageId,
                    targetChatItem.IsGroupChat,
                    targetChatItem.UserId,
                    targetChatItem.ChatTitle,
                    targetChatItem.FirstUnreadId);
            }
            else
            {
                App.ErideMessage.AddMessage($"Чат {data.ChatId} не найден в списке, обновляем...", new Erida { Type = MType.Debug });
                _ = _chatListCtrl.ChatUpdateAsync();
            }
        }
    }
}
