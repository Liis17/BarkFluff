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
                            Message = new Proto.Messages.OutgoingMessage { Text = letter.Text, FilesIds = { letter.FilesId } },
                        });
                    }
                    else
                    {
                        chatId = string.Empty;
                        response = await MessagesAC!.SendMessageAsync(new Proto.Messages.SendMessageRequest
                        {
                            UserId = long.Parse(options.recipient),
                            Message = new Proto.Messages.OutgoingMessage { Text = letter.Text, FilesIds = { letter.FilesId } },
                        });
                    }

                    var sentMessage = new MessageModel
                    {
                        MessageId = response.Message.Id,
                        ChatId = chatId,
                        Text = response.Message.Content.Text,
                        Attachments = response.Message.Content.Attachments.Select(a => new AttachmentsModel
                        {
                            Id = a.Id,
                            Type = a.Type,
                            PreviewUrl = a.PreviewUrl,
                            FileId = a.FileId,
                            PreviewFileId = a.PreviewFileId,
                            FileName = a.FileName,
                            Size = a.AttachmentSize,
                        }).ToList(),
                        SenderId = response.Message.SenderId,
                        SentAt = response.Message.SentAt,
                        Type = response.Message.Type,
                        ReadBy = response.Message.ReadBy.ToList(),
                    };

                    return (new ErrorReturner(true), sentMessage);
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
                    // TODO: Реализовать создание группового чата (Backend task)
                    await Task.CompletedTask;
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
                return (false, "Ошибка создания Gruppового чата");
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
                    return (new ErrorReturner(true), response.Messages.Select(m => new MessageModel
                    {
                        MessageId = m.Id,
                        ChatId = chatId,
                        Text = m.Content.Text,
                        Attachments = m.Content.Attachments.Select(a => new AttachmentsModel
                        {
                            Id = a.Id,
                            Type = a.Type,
                            PreviewUrl = a.PreviewUrl,
                            FileId = a.FileId,
                            PreviewFileId = a.PreviewFileId,
                            FileName = a.FileName,
                            Size = a.AttachmentSize,
                        }).ToList(),
                        SenderId = m.SenderId,
                        SentAt = m.SentAt,
                        Type = m.Type,
                        ReadBy = m.ReadBy.ToList(),
                    }).ToList());
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
                    return (new ErrorReturner(true), response.Messages.Select(m => new MessageModel
                    {
                        MessageId = m.Id,
                        ChatId = chatId,
                        Text = m.Content.Text,
                        Attachments = m.Content.Attachments.Select(a => new AttachmentsModel
                        {
                            Id = a.Id,
                            Type = a.Type,
                            PreviewUrl = a.PreviewUrl,
                            FileId = a.FileId,
                            PreviewFileId = a.PreviewFileId,
                            FileName = a.FileName,
                            Size = a.AttachmentSize,
                        }).ToList(),
                        SenderId = m.SenderId,
                        SentAt = m.SentAt,
                        Type = m.Type,
                        ReadBy = m.ReadBy.ToList(),
                    }).ToList());
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
    }
}
