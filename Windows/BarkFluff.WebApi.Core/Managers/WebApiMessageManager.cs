using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для работы с сообщениями.
    /// </summary>
    internal class WebApiMessageManager : WebApiBase
    {
        private const int DefaultPageSize = 50;
        private readonly WebApi _webApi;

        public WebApiMessageManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Преобразует серверное сообщение в модель клиента.
        /// </summary>
        internal static MessageModel MapMessage(Proto.Shared.Message message, string chatId) => new MessageModel
        {
            MessageId = message.Id,
            ChatId = chatId,
            Text = message.Content.Text,
            Attachments = message.Content.Attachments.Select(MapAttachment).ToList(),
            SenderId = message.SenderId,
            SentAt = message.SentAt,
            Type = message.Type,
            ReadBy = message.ReadBy.ToList(),
            IsEdited = message.IsEdited,
            EditedAt = message.EditedAt,
        };

        /// <summary>
        /// Преобразует серверное вложение в модель клиента. Вложения внутри пересланного
        /// сообщения маппятся без ForwardedMessage — сервер рекурсию не присылает.
        /// </summary>
        private static AttachmentsModel MapAttachment(Proto.Shared.MessageAttachment a) => new AttachmentsModel
        {
            Id = a.Id,
            Type = a.Type,
            PreviewUrl = a.PreviewUrl,
            FileId = a.FileId,
            PreviewFileId = a.PreviewFileId,
            FileName = a.FileName,
            Size = a.AttachmentSize,
            ImageWidth = a.ImageWidth,
            ImageHeight = a.ImageHeight,
            ForwardedMessage = a.ForwardedMessage is null ? null : new ForwardedMessageModel
            {
                AuthorName = a.ForwardedMessage.AuthorName,
                OriginalMessageId = a.ForwardedMessage.OriginalMessageId,
                Text = a.ForwardedMessage.Text,
                Attachments = a.ForwardedMessage.Attachments.Select(inner => new AttachmentsModel
                {
                    Id = inner.Id,
                    Type = inner.Type,
                    PreviewUrl = inner.PreviewUrl,
                    FileId = inner.FileId,
                    PreviewFileId = inner.PreviewFileId,
                    FileName = inner.FileName,
                    Size = inner.AttachmentSize,
                    ImageWidth = inner.ImageWidth,
                    ImageHeight = inner.ImageHeight,
                }).ToList(),
            },
        };

        /// <summary>
        /// Получение списка чатов
        /// </summary>
        /// <param name="globalParam"></param>
        /// <returns></returns>
        public async Task<(ErrorReturner error, List<Proto.Messages.Chat>? chats)> GetChats(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListChatsAsync(new Proto.Messages.ListChatsRequest
                    {
                        Pagination = new Proto.Shared.PageRequest { Size = DefaultPageSize },
                    });

                    var chatsList = response.Chats.ToList();

                    return (new ErrorReturner(true), chatsList);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтвержден."), null);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, "Ошибка получения чатов"), null);
            }
        }

        /// <summary>
        /// Получить информацию о чате по его идентификатору.
        /// </summary>
        public async Task<(ErrorReturner error, ChatInfo chatInfo)> GetChatInfo(GlobalParam globalParam, string chatId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.GetChatInfoAsync(new Proto.Messages.GetChatInfoRequest
                    {
                        ChatId = chatId,
                    });
                    var chatInfo = new ChatInfo
                    {
                        ChatId = chatId,
                        Members = response.MembersId.ToList(),
                        Title = response.Title,
                        Picture = response.Picture,
                        IsGroup = response.IsGroupChat,
                        LastMessageId = response.LastMessageId,
                        FirstUnreadId = response.FirstUnreadMessageId,
                        CountUnread = response.CountUnread,
                    };

                    return (new ErrorReturner(true), chatInfo);
                }, globalParam);

            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтвержден"), new ChatInfo());
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата"), new ChatInfo());
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return (new ErrorReturner(false, "Чат не найден"), new ChatInfo());
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), new ChatInfo());
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, "Ошибка получения информации о чате"), new ChatInfo());
            }
        }

        /// <summary>
        /// Отправка сообщения в чат
        /// </summary>
        public async Task<(ErrorReturner error, MessageModel? message)> SendMessage(GlobalParam globalParam, (bool isUserId, string recipient) options, ForwardingLetter letter)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    Proto.Messages.SendMessageResponse response;
                    string chatId;
                    if (!options.isUserId)
                    {
                        chatId = options.recipient;
                        response = await MessagesAC!.SendMessageAsync(new Proto.Messages.SendMessageRequest
                        {
                            ChatId = options.recipient,
                            Message = new Proto.Messages.OutgoingMessage { Text = letter.Text, FilesIds = { letter.FilesId }, ForwardedMessageId = letter.ForwardedMessageId },
                        });
                    }
                    else
                    {
                        chatId = string.Empty;
                        response = await MessagesAC!.SendMessageAsync(new Proto.Messages.SendMessageRequest
                        {
                            UserId = long.Parse(options.recipient),
                            Message = new Proto.Messages.OutgoingMessage { Text = letter.Text, FilesIds = { letter.FilesId }, ForwardedMessageId = letter.ForwardedMessageId },
                        });
                    }

                    return (new ErrorReturner(true), MapMessage(response.Message, chatId));
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата."), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка отправки сообщения"), null);
            }
        }

        public async Task<(bool, string?)> CreateGroupChat(GlobalParam globalParam, string chatName, List<long> userIds)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new Proto.Messages.CreateGroupChatRequest
                    {
                        Title = chatName
                    };
                    request.UserIds.AddRange(userIds);
                    await MessagesAC!.CreateGroupChatAsync(request);
                    return (true, string.Empty);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (false, "Неверный идентификатор чата");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.UserNotMemberChatException)
            {
                return (false, "Пользователь не является участником чата");
            }
            catch (Exception)
            {
                return (false, "Ошибка создания группового чата");
            }
        }

        public async Task<(bool, string?)> CreateChat(GlobalParam globalParam, string userId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.CreateGroupChatAsync(new Proto.Messages.CreateGroupChatRequest
                    {
                        // TODO: Добавить UserIds в API запрос (Backend task)
                    });

                    return (true, string.Empty);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (false, "Неверный идентификатор чата");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.UserNotMemberChatException)
            {
                return (false, "Пользователь не является участником чата");
            }
            catch (Exception)
            {
                return (false, "Ошибка создания чата");
            }
        }

        /// <summary>
        /// Метод для получения сообщений из чата.
        /// </summary>
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessages(GlobalParam globalParam, string chatId, long fromMessageId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListMessagesAsync(new Proto.Messages.ListMessagesRequest { ChatId = chatId, Count = DefaultPageSize, FromMessageId = fromMessageId });
                    if (response.Messages.Count == 0)
                    {
                        return (new ErrorReturner(false, "Нет сообщений в этом чате", 1), null);
                    }
                    return (new ErrorReturner(true), response.Messages.Select(m => MapMessage(m, chatId)).ToList());
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения сообщений"), null);
            }
        }

        /// <summary>
        /// Метод для получения сообщений из чата с двусторонней пагинацией.
        /// Возвращает сообщения до и после указанного сообщения.
        /// </summary>
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesWithOffset(GlobalParam globalParam, string chatId, long fromMessageId, int offsetBefore, int offsetAfter)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListMessagesAsync(new Proto.Messages.ListMessagesRequest
                    {
                        ChatId = chatId,
                        FromMessageId = fromMessageId,
                        OffsetBefore = offsetBefore,
                        OffsetAfter = offsetAfter
                    });
                    if (response.Messages.Count == 0)
                    {
                        return (new ErrorReturner(false, "Нет сообщений в этом чате", 1), null);
                    }
                    return (new ErrorReturner(true), response.Messages.Select(m => MapMessage(m, chatId)).ToList());
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения сообщений"), null);
            }
        }

        /// <summary>
        /// Отметка сообщения как прочитанного.
        /// </summary>
        public async Task<ErrorReturner> MarkMessageAsRead(GlobalParam globalParam, List<long> messageId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.MarkAsReadAsync(new Proto.Messages.MarkAsReadRequest { MessageIds = { messageId } });
                    return (new ErrorReturner(true));
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return new ErrorReturner(false, "Неверный идентификатор чата.");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.MessageNotFoundException)
            {
                return new ErrorReturner(false, "Сообщение не найдено.");
            }
            catch (Exception ex)
            {
                return new ErrorReturner(false, "Ошибка отметки сообщения как прочитанного");
            }
        }

        /// <summary>
        /// Получить идентификатор персонального чата с указанным пользователем.
        /// </summary>
        public async Task<(ErrorReturner error, string chatId)> GetPersonChatId(GlobalParam globalParam, long userId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.GetPersonChatIdAsync(new Proto.Messages.GetPersonChatIdRequest { UserId = userId });
                    return (new ErrorReturner(true), response.ChatId);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UserNotFoundException)
            {
                return (new ErrorReturner(false, "Пользователь не найден"), string.Empty);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return (new ErrorReturner(false, "Чат с данным пользователем не найден"), string.Empty);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата"), string.Empty);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), string.Empty);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, "Ошибка получения идентификатора чата"), string.Empty);
            }
        }

        public async Task<(ErrorReturner error, List<Proto.Messages.ListChatMembersResponse.Types.DetailedChatMemberInfo>? members, int totalCount)> ListChatMembers(
            GlobalParam globalParam, string chatId, int offset = 0, int size = 50)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListChatMembersAsync(new Proto.Messages.ListChatMembersRequest
                    {
                        ChatId = chatId,
                        Pagination = new Proto.Shared.PageRequest { Offset = offset, Size = size }
                    });
                    return (new ErrorReturner(true), response.ChatMembers.ToList(), response.TotalCount);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return (new ErrorReturner(false, "Чат не найден"), null, 0);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null, 0);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения участников чата"), null, 0);
            }
        }

        public async Task<(ErrorReturner error, List<Proto.Messages.ChatAttachmentInfo>? attachments, int totalCount)> ListChatAttachments(
            GlobalParam globalParam,
            string chatId,
            Proto.Shared.MessageAttachmentType attachmentType = Proto.Shared.MessageAttachmentType.Unknown,
            bool sortDescending = true,
            int offset = 0,
            int size = 50)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListChatAttachmentsAsync(new Proto.Messages.ListChatAttachmentsRequest
                    {
                        ChatId = chatId,
                        AttachmentType = attachmentType,
                        SortDescending = sortDescending,
                        Pagination = new Proto.Shared.PageRequest { Offset = offset, Size = size }
                    });
                    return (new ErrorReturner(true), response.Attachments.ToList(), response.TotalCount);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return (new ErrorReturner(false, "Чат не найден"), null, 0);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null, 0);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения вложений чата"), null, 0);
            }
        }

        /// <summary>
        /// Редактирование текста и списка файлов своего сообщения.
        /// </summary>
        public async Task<(ErrorReturner error, MessageModel? message)> EditMessage(
            GlobalParam globalParam, string chatId, long messageId, string text, List<string>? fileIds = null)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new Proto.Messages.EditMessageRequest
                    {
                        MessageId = messageId,
                        Text = text,
                    };
                    if (fileIds != null)
                        request.FilesIds.AddRange(fileIds);

                    var response = await MessagesAC!.EditMessageAsync(request);
                    return (new ErrorReturner(true), MapMessage(response.Message, chatId));
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.MessageNotFoundException)
            {
                return (new ErrorReturner(false, "Сообщение не найдено"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return (new ErrorReturner(false, "Редактировать можно только свои сообщения"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.MessageTextTooLongException)
            {
                return (new ErrorReturner(false, "Текст сообщения слишком длинный"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка редактирования сообщения"), null);
            }
        }

        /// <summary>
        /// Удаление своего сообщения.
        /// </summary>
        public async Task<ErrorReturner> DeleteMessage(GlobalParam globalParam, long messageId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.DeleteMessageAsync(new Proto.Messages.DeleteMessageRequest { MessageId = messageId });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.MessageNotFoundException)
            {
                return new ErrorReturner(false, "Сообщение не найдено");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return new ErrorReturner(false, "Удалять можно только свои сообщения");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return new ErrorReturner(false, "Нет доступа к чату");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка удаления сообщения");
            }
        }

        /// <summary>
        /// Закрепить сообщение в чате.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Shared.PinnedMessageInfo? pinned)> PinMessage(
            GlobalParam globalParam, string chatId, long messageId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.PinMessageAsync(new Proto.Messages.PinMessageRequest
                    {
                        ChatId = chatId,
                        MessageId = messageId
                    });
                    return ((ErrorReturner, Proto.Shared.PinnedMessageInfo?))(new ErrorReturner(true), response.Pinned);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.TooManyPinnedMessagesException)
            {
                return (new ErrorReturner(false, "Достигнут лимит закреплённых сообщений в чате"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.MessageNotFoundException)
            {
                return (new ErrorReturner(false, "Сообщение не найдено"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return (new ErrorReturner(false, "Нет прав на закрепление сообщений"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка закрепления сообщения"), null);
            }
        }

        /// <summary>
        /// Открепить сообщение.
        /// </summary>
        public async Task<ErrorReturner> UnpinMessage(GlobalParam globalParam, string chatId, long messageId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.UnpinMessageAsync(new Proto.Messages.UnpinMessageRequest
                    {
                        ChatId = chatId,
                        MessageId = messageId
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.MessageNotFoundException)
            {
                return new ErrorReturner(false, "Сообщение не найдено");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return new ErrorReturner(false, "Нет прав на открепление сообщений");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return new ErrorReturner(false, "Нет доступа к чату");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка открепления сообщения");
            }
        }

        /// <summary>
        /// Список закреплённых сообщений чата.
        /// </summary>
        public async Task<(ErrorReturner error, List<Proto.Shared.PinnedMessageInfo>? pinned, int totalCount)> ListPinnedMessages(
            GlobalParam globalParam, string chatId, int offset = 0, int size = DefaultPageSize)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListPinnedMessagesAsync(new Proto.Messages.ListPinnedMessagesRequest
                    {
                        ChatId = chatId,
                        Pagination = new Proto.Shared.PageRequest { Offset = offset, Size = size }
                    });
                    return (new ErrorReturner(true), response.Pinned.ToList(), response.TotalCount);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return (new ErrorReturner(false, "Чат не найден"), null, 0);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null, 0);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения закреплённых сообщений"), null, 0);
            }
        }

        /// <summary>
        /// Открепить все сообщения чата. Возвращает количество откреплённых.
        /// </summary>
        public async Task<(ErrorReturner error, int unpinnedCount)> UnpinAll(GlobalParam globalParam, string chatId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.UnpinAllAsync(new Proto.Messages.UnpinAllRequest { ChatId = chatId });
                    return (new ErrorReturner(true), response.UnpinnedCount);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return (new ErrorReturner(false, "Нет прав на открепление сообщений"), 0);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), 0);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка открепления сообщений"), 0);
            }
        }

        /// <summary>
        /// Добавить пользователя в групповой чат.
        /// </summary>
        public async Task<ErrorReturner> AddUser(GlobalParam globalParam, string chatId, long userId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.AddUserAsync(new Proto.Messages.AddUserRequest
                    {
                        ChatId = chatId,
                        UserId = userId
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.UserAlreadyMemberChatException)
            {
                return new ErrorReturner(false, "Пользователь уже состоит в чате");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.IsNotGroupChatException)
            {
                return new ErrorReturner(false, "Добавлять участников можно только в групповой чат");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return new ErrorReturner(false, "Нет прав на добавление участников");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return new ErrorReturner(false, "Чат не найден");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка добавления пользователя в чат");
            }
        }

        /// <summary>
        /// Изменить название и/или обложку группового чата. Пустая строка = поле не меняется.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.Chat? chat)> UpdateGroupChat(
            GlobalParam globalParam, string chatId, string title = "", string pictureFileId = "")
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.UpdateGroupChatAsync(new Proto.Messages.UpdateGroupChatRequest
                    {
                        ChatId = chatId,
                        Title = title,
                        PictureFileId = pictureFileId
                    });
                    return ((ErrorReturner, Proto.Messages.Chat?))(new ErrorReturner(true), response.Chat);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.IsNotGroupChatException)
            {
                return (new ErrorReturner(false, "Изменять можно только групповой чат"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return (new ErrorReturner(false, "Нет прав на изменение чата"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.FileHasNotGroupPictureTypeException)
            {
                return (new ErrorReturner(false, "Файл не подходит в качестве обложки чата"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return (new ErrorReturner(false, "Чат не найден"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка изменения чата"), null);
            }
        }

        public async Task<ErrorReturner> KickUser(GlobalParam globalParam, string chatId, long userId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.KickUserAsync(new Proto.Messages.KickUserRequest
                    {
                        ChatId = chatId,
                        UserId = userId
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotFoundException)
            {
                return new ErrorReturner(false, "Чат не найден");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return new ErrorReturner(false, "Нет прав для исключения пользователя из чата");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.UserNotMemberChatException)
            {
                return new ErrorReturner(false, "Пользователь не является участником чата");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка исключения пользователя из чата");
            }
        }
    }
}
