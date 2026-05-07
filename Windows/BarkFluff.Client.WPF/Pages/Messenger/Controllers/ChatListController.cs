using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за загрузку списка чатов, обновление профиля в заголовке
    /// и анимированное переупорядочивание <see cref="ChatItem"/> при новых сообщениях.
    /// </summary>
    public sealed class ChatListController
    {
        private readonly MessengerPage _page;
        private readonly StackPanel _chatList;
        private readonly CachedAvatar _avatarTitleWindowButton;

        // Буфер для хранения chatId -> lastMessageId для быстрого обновления статуса прочтения
        private readonly Dictionary<string, long> _chatLastMessageBuffer = new();
        private readonly object _chatBufferLock = new();

        public string? CurrentUserAvatarFileId { get; private set; }

        public ChatListController(MessengerPage page, StackPanel chatList, CachedAvatar avatarTitleWindowButton)
        {
            _page = page;
            _chatList = chatList;
            _avatarTitleWindowButton = avatarTitleWindowButton;
        }

        public async Task ChatUpdateAsync()
        {
            var response = await App.ServerCommunication.GetChats(App.GParam);
            if (response.error == null) { return; }
            if (!response.error.IsSuccess)
            {
                App.ErideMessage.AddMessage($"{response.error.ErrorMessage}", new Erida { Type = MType.Warning });
                return;
            }

            // Сортировка чатов по времени последнего сообщения (новые выше).
            var sortedChats = response.chats
                .Where(c => !c.IsGroupChat)
                .OrderByDescending(chat => chat.LastMessage?.SentAt.ToDateTime() ?? DateTime.MinValue)
                .ToList();

            // Снапшот существующих ChatItem-ов: переиспользуем их при diff —
            // не пересоздаём контролы, не вызываем Unloaded/Loaded и не отписываем CachedAvatar.
            var existingItems = _chatList.Children.OfType<ChatItem>().ToDictionary(ci => ci.ChatId);

            // Перестраиваем буфер последних сообщений (под локом).
            var newBuffer = new Dictionary<string, long>(sortedChats.Count);
            var personalUserIds = new List<long>(sortedChats.Count);
            var orderedItems = new List<ChatItem>(sortedChats.Count);

            foreach (var item in sortedChats)
            {
                var title = item.Title.Trim();
                var membersId = item.Members.Select(m => m.UserId).ToList();
                bool isSelfChat = (item.Members.Count >= 2 &&
                                   App.GParam.UserId == item.Members[0].UserId &&
                                   App.GParam.UserId == item.Members[1].UserId);

                var isRead = ChatItem.ReadingStatus.ForMe;
                long userId;
                string avatar;
                if (isSelfChat)
                {
                    isRead = ChatItem.ReadingStatus.My;
                    title = "Избранное";
                    avatar = "SavedChat";
                    userId = App.GParam.UserId;
                }
                else
                {
                    avatar = string.IsNullOrEmpty(item.Picture) ? "UserWithoutAvatar" : item.Picture;
                    membersId.Remove(App.GParam.UserId);
                    userId = membersId.FirstOrDefault();

                    if (userId == 0)
                    {
                        App.ErideMessage.AddMessage($"Ошибка: нет доступных userId для чата {item.Id}", new Erida { Type = MType.Error });
                        continue;
                    }
                }

                ChatItem chatItem;
                if (existingItems.TryGetValue(item.Id, out var existing))
                {
                    // Чат уже в UI — обновляем in-place.
                    existing.ApplySnapshot(avatar, title, item.LastMessage, item.CountUnread, item.FirstUnreadMessageId);
                    existingItems.Remove(item.Id);
                    chatItem = existing;
                }
                else
                {
                    chatItem = new ChatItem(
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

                    if (item.LastMessage != null)
                    {
                        chatItem.TransferMessage = new MessageModel
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
                        chatItem.UpdateMessage();
                    }
                }

                orderedItems.Add(chatItem);

                if (item.LastMessage != null)
                {
                    newBuffer[item.Id] = item.LastMessage.Id;
                }
                if (!isSelfChat && userId > 0)
                {
                    personalUserIds.Add(userId);
                }
            }

            // Удаляем ChatItem-ы для чатов, которых больше нет в ответе.
            foreach (var (_, stale) in existingItems)
            {
                _chatList.Children.Remove(stale);
            }

            // Применяем новый порядок без полного Clear: двигаем только реально съехавшие элементы.
            for (int i = 0; i < orderedItems.Count; i++)
            {
                var target = orderedItems[i];
                var currentIdx = _chatList.Children.IndexOf(target);
                if (currentIdx < 0)
                {
                    _chatList.Children.Insert(i, target);
                }
                else if (currentIdx != i)
                {
                    _chatList.Children.RemoveAt(currentIdx);
                    _chatList.Children.Insert(i, target);
                }
            }

            // Обновляем буфер последних сообщений.
            lock (_chatBufferLock)
            {
                _chatLastMessageBuffer.Clear();
                foreach (var (k, v) in newBuffer)
                {
                    _chatLastMessageBuffer[k] = v;
                }
            }

            // Один батч-запрос онлайн-статусов вместо N индивидуальных из ChatItem_Loaded.
            if (personalUserIds.Count > 0)
            {
                _ = FetchOnlineStatusesBatchAsync(personalUserIds);
            }
        }

        /// <summary>
        /// Один батч-запрос статусов для всех видимых приватных чатов.
        /// Заменяет N индивидуальных <c>FetchAndUpdateOnlineStatusAsync</c>
        /// из <c>ChatItem.Loaded</c>, которые при первой загрузке списка
        /// устраивали шторм gRPC-вызовов.
        /// </summary>
        private async Task FetchOnlineStatusesBatchAsync(List<long> userIds)
        {
            try
            {
                var distinctIds = userIds.Distinct().ToList();
                if (distinctIds.Count == 0) return;

                var (error, response) = await App.ServerCommunication.GetOnlineStatus(distinctIds, App.GParam);
                if (!error.IsSuccess || response == null) return;

                await _page.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var status in response.UsersStatuses)
                    {
                        App.OnlineStatusService.UpdateCachedStatus(status);

                        foreach (var child in _chatList.Children)
                        {
                            if (child is ChatItem ci && ci.UserId == status.UserId)
                            {
                                ci.UpdateOnlineStatus(
                                    status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline,
                                    status.LastSeen?.ToDateTime());
                                break;
                            }
                        }
                    }
                });
            }
            catch
            {
                // Stream-обновления подхватят актуальные статусы — игнорируем разовую ошибку.
            }
        }

        public async Task UserInfoUpdateAsync()
        {
            var response = await App.ServerCommunication.GetUserData(App.GParam);
            if (response.Error == null) { return; }
            if (!response.Error.IsSuccess)
            {
                App.ErideMessage.AddMessage($"{response.Error.ErrorMessage}", new Erida { Type = MType.Warning });
                return;
            }

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

            // Обновляем аватар текущего пользователя в заголовке
            if (!string.IsNullOrEmpty(App.GParam.PictureUrl))
            {
                CurrentUserAvatarFileId = FileCacheService.ExtractFileIdFromUrl(App.GParam.PictureUrl);
                if (!string.IsNullOrEmpty(CurrentUserAvatarFileId))
                {
                    // Сбрасываем тип — LoadImage вернётся сразу (тип UserWithoutAvatar)
                    _avatarTitleWindowButton.AvatarType = AvatarType.UserWithoutAvatar;
                    // Устанавливаем URL и FileId пока тип UserWithoutAvatar (LoadImage вернётся сразу)
                    _avatarTitleWindowButton.FileUrl = App.GParam.PictureUrl;
                    _avatarTitleWindowButton.FileId = CurrentUserAvatarFileId;
                    // Переключаем тип — это запустит LoadImage с уже установленными FileId и FileUrl
                    _avatarTitleWindowButton.AvatarType = AvatarType.Image;
                }
                else
                {
                    _avatarTitleWindowButton.FileId = null;
                    _avatarTitleWindowButton.FileUrl = null;
                    _avatarTitleWindowButton.AvatarType = AvatarType.UserWithoutAvatar;
                }
            }
            else
            {
                _avatarTitleWindowButton.FileId = null;
                _avatarTitleWindowButton.FileUrl = null;
                _avatarTitleWindowButton.AvatarType = AvatarType.UserWithoutAvatar;
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
            foreach (var child in _chatList.Children)
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
        /// Пытается получить ID последнего сообщения для указанного чата из буфера.
        /// Используется Realtime-контроллером для быстрого решения, обновлять ли чат-лист.
        /// </summary>
        public bool TryGetLastMessageIdForChat(string chatId, out long lastMessageId)
        {
            lock (_chatBufferLock)
            {
                return _chatLastMessageBuffer.TryGetValue(chatId, out lastMessageId);
            }
        }

        /// <summary>
        /// Сохраняет ID последнего сообщения в буфере (например, при добавлении нового чата).
        /// </summary>
        public void SetLastMessageIdForChat(string chatId, long lastMessageId)
        {
            lock (_chatBufferLock)
            {
                _chatLastMessageBuffer[chatId] = lastMessageId;
            }
        }

        /// <summary>
        /// Анимирует перемещение ChatItem'ов при изменении их позиции в списке
        /// </summary>
        private async void AnimateChatItemReordering()
        {
            await _page.Dispatcher.InvokeAsync(async () =>
            {
                // Получаем текущие позиции всех ChatItem
                var currentPositions = new Dictionary<ChatItem, double>();
                foreach (var child in _chatList.Children)
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
                        var point = chatItem.TransformToAncestor(_chatList).Transform(new Point(0, 0));
                        currentPositions[chatItem] = point.Y;
                    }
                }

                // Сортируем ChatItem по времени последнего сообщения
                var sortedChatItems = _chatList.Children.OfType<ChatItem>()
                    .OrderByDescending(chatItem => chatItem.TransferMessage?.SentAt.ToDateTime() ?? DateTime.MinValue)
                    .ToList();

                // Проверяем, нужно ли вообще что-то менять
                bool needsReordering = false;
                for (int i = 0; i < sortedChatItems.Count; i++)
                {
                    if (_chatList.Children.IndexOf(sortedChatItems[i]) != i)
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
                _chatList.Children.Clear();
                foreach (var chatItem in sortedChatItems)
                {
                    _chatList.Children.Add(chatItem);
                }

                // Даём время на layout update
                await Task.Delay(10);
                _chatList.UpdateLayout();

                // Вычисляем новые позиции и запускаем анимацию
                foreach (var chatItem in sortedChatItems)
                {
                    if (!currentPositions.ContainsKey(chatItem))
                        continue;

                    var oldPosition = currentPositions[chatItem];
                    var newPoint = chatItem.TransformToAncestor(_chatList).Transform(new Point(0, 0));
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
    }
}
