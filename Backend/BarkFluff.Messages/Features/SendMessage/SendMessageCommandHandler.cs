using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Queue.Federation;

using MediatR;

using Message = BarkFluff.Messages.Domain.Message;
using MessageAttachment = BarkFluff.Messages.Domain.MessageAttachment;
using MessageContent = BarkFluff.Messages.Domain.MessageContent;
using MessageContentType = BarkFluff.Messages.Domain.MessageContentType;

namespace BarkFluff.Messages.Features.SendMessage;

using Infrastructure;

public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, SendMessageResponse>
{
    private const int MaxTextLength = 4096;
    private const int MaxAttachmentsPerMessage = 10;

    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UserContext _userContext;
    private readonly ChatCache _chatCache;
    private readonly MessagesStorage _messagesStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    private readonly Dictionary<UploadFileType, Domain.MessageAttachmentType> _attachmentMap =
        new()
        {
            { UploadFileType.MessageAttachmentImage, Domain.MessageAttachmentType.Image },
            { UploadFileType.MessageAttachmentDocument, Domain.MessageAttachmentType.Document },
            { UploadFileType.MessageAttachmentGif, Domain.MessageAttachmentType.Gif },
            { UploadFileType.MessageAttachmentVideo, Domain.MessageAttachmentType.Video },
            { UploadFileType.MessageAttachmentAudio, Domain.MessageAttachmentType.Audio },
            { UploadFileType.MessageAttachmentVoice, Domain.MessageAttachmentType.Voice },
            { UploadFileType.MessageAttachmentSticker, Domain.MessageAttachmentType.Sticker }
        };

    public SendMessageCommandHandler(ChatsStorage chatsStorage, UsersServerApi.UsersServerApiClient usersServerApiClient,
        UserContext userContext, FilesServerApi.FilesServerApiClient filesServerApiClient, ChatCache chatCache, MessagesStorage messagesStorage,
        MessageQueueSender messageQueueSender, MetricsCollector metrics, ILogger<SendMessageCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _filesServerApiClient = filesServerApiClient;
        _chatCache = chatCache;
        _messagesStorage = messagesStorage;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SendMessageResponse> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        // Серверный путь (SendMessageServer от сервиса Bots) передаёт отправителя явно, клиентский — из токена.
        var senderId = request.SenderId ?? _userContext.UserId;

        _logger.LogInformation(
            "Начало отправки сообщения от пользователя {UserId} в чат {ChatId} или пользователю {TargetUserId}",
            senderId,
            request.ChatId,
            request.UserId
        );

        if (request.Message is null ||
            request.Message.Text is null &&
            request.Message.FileIds is null &&
            request.Message.ForwardedMessageId is null)
        {
            _logger.LogWarning(
                "Попытка отправки пустого сообщения от пользователя {UserId}",
                senderId
            );
            throw new MessageNotContainContextException();
        }

        if (request.Message.Text is { Length: > MaxTextLength })
        {
            _logger.LogWarning(
                "Текст сообщения от пользователя {UserId} превышает лимит {MaxTextLength} символов: {ActualLength}",
                senderId,
                MaxTextLength,
                request.Message.Text.Length
            );
            throw new MessageTextTooLongException();
        }

        if (request.Message.FileIds is { } fileIds && fileIds.Count > MaxAttachmentsPerMessage)
        {
            _logger.LogWarning(
                "Сообщение от пользователя {UserId} содержит {AttachmentCount} вложений (лимит: {MaxAttachments})",
                senderId,
                fileIds.Count,
                MaxAttachmentsPerMessage
            );
            throw new TooManyAttachmentsException();
        }

        var chatId = request.ChatId;

        // Федеративный контекст исходящего сообщения (этап 2.3). Заполняется только при отправке в fed-DM.
        var isFederated = false;
        var federatedId = Guid.NewGuid();
        var senderUuid = Guid.Empty;
        List<FederatedParticipant> remoteParticipants = new();
        var isFirstMessageInFedChat = false;
        Guid? fedInitiatorUuid = null;
        Guid? fedInviteeUuid = null;
        string? senderFid = null;

        if (chatId is null && request.UserId is null && request.UserUuid is null)
        {
            _logger.LogWarning(
                "Не указан ни ChatId, ни UserId, ни UserUuid для отправки сообщения от пользователя {UserId}",
                senderId
            );
            throw new SourceForSendMessageNotSetException();
        }

        // fed-ветка: отправка по UUID remote-получателя.
        if (chatId is null && request.UserUuid is not null)
        {
            var targetUuid = request.UserUuid.Value;

            // Резолв: это локальный пользователь → ordinary personal чат; remote → fed-DM.
            var byUuidResp = await _usersServerApiClient.GetUsersByUuidAsync(
                new GetUsersByUuidRequest { Uuids = { targetUuid.ToString() } });
            var target = byUuidResp.Users.FirstOrDefault();
            if (target is null || !target.Found)
                throw new RemoteUserNotResolvedException();

            if (!target.IsRemote)
            {
                // Локальный получатель с известным numeric id → переиспользуем обычный путь ниже.
                request.UserId = target.UserId;
                request.UserUuid = null;
            }
            else
            {
                if (string.IsNullOrEmpty(target.ServerName))
                    throw new RemoteUserNotResolvedException();

                // Свой UUID — из Users.GetById (локальный профиль отправителя).
                var selfResp = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = senderId });
                if (!Guid.TryParse(selfResp.User.Uuid, out senderUuid) || senderUuid == Guid.Empty)
                    throw new RemoteUserNotResolvedException();

                senderFid = $"@{selfResp.User.Username}:{target.ServerName}";

                var (uuidLow, uuidHigh) = Features.Federation.FederatedUuidPair.Normalize(senderUuid, targetUuid);

                var existing = await _chatsStorage.FindActiveFederatedChatByUuidPairAsync(uuidLow, uuidHigh);
                if (existing is not null)
                {
                    chatId = existing.Id;
                }
                else
                {
                    var newChatId = Guid.NewGuid();
                    var raceResult = await _chatsStorage.CreateFederatedChatAsync(
                        newChatId,
                        senderId,
                        senderUuid,
                        targetUuid,
                        target.ServerName,
                        uuidLow,
                        uuidHigh);
                    chatId = raceResult.Id;

                    if (raceResult.Id == newChatId)
                    {
                        isFirstMessageInFedChat = true;
                        fedInitiatorUuid = senderUuid;
                        fedInviteeUuid = targetUuid;
                        _metrics.Increment("chats_created_federated");
                        _logger.LogInformation("Создан fed-чат {ChatId} между {SenderUuid} и {TargetUuid}",
                            chatId.Value, senderUuid, targetUuid);
                    }
                    // иначе — проиграли гонку одновременного создания: переиспользуем чат победителя,
                    // он уже отправил ChatCreated.
                }

                isFederated = true;
                remoteParticipants = new List<FederatedParticipant>
                {
                    new() { Uuid = targetUuid, ServerName = target.ServerName },
                };
            }
        }

        if (chatId != null)
        {
            _logger.LogDebug("Проверка доступа пользователя {UserId} к чату {ChatId}", senderId, chatId.Value);

            var hasAccess = await _chatsStorage.CheckAccessToChat(chatId.Value, senderId);

            if (!hasAccess)
            {
                _logger.LogWarning(
                    "Пользователь {UserId} не имеет доступа к чату {ChatId}",
                    senderId,
                    chatId.Value
                );
                throw new NoAccessToChatException();
            }

            // Fed-чат, отклонённый partner-нодой (privacy DenyFederatedDm, этап 2.5) — понятная
            // ошибка вместо тихой отправки в чат, который вторая сторона никогда не увидит.
            var federatedStatus = await _chatsStorage.GetFederatedStatusAsync(chatId.Value);
            if (federatedStatus == FederatedStatus.Rejected)
            {
                _logger.LogWarning(
                    "Попытка отправки в отклонённый fed-чат {ChatId} пользователем {UserId}",
                    chatId.Value, senderId);
                throw new FederatedDmRejectedException();
            }
        }

        if (chatId is null)
        {
            _logger.LogDebug(
                "Создание или получение личного чата между пользователями {UserId} и {TargetUserId}",
                senderId,
                request.UserId
            );

            // Получаем пользователя по ID
            var personRepose = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId!.Value });

            var chatIdWithPerson = await _chatsStorage.GetUserChatIdWithPerson(personRepose.User.Id, senderId);

            if (chatIdWithPerson is null && request.SenderId is not null && !request.AllowChatCreation)
            {
                // Серверный путь (боты): чат должен уже существовать — бот не пишет первым.
                _logger.LogWarning(
                    "Серверная отправка от {SenderId} пользователю {TargetUserId} отклонена: личного чата не существует",
                    senderId,
                    request.UserId
                );
                throw new NoAccessToChatException();
            }

            if (chatIdWithPerson is null)
            {
                _logger.LogInformation(
                    "Создание нового личного чата между пользователями {UserId} и {TargetUserId}",
                    senderId,
                    personRepose.User.Id
                );

                var createdChat = await _chatsStorage.CreatePersonChat(senderId, personRepose.User.Id);

                chatId = createdChat.Id;

                var userResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = senderId });

                // Кэшируем аватарочки и имена
                await _chatCache.SetChatName(chatId.Value, senderId, $"{personRepose.User.FirstName} {personRepose.User.LastName}");
                await _chatCache.SetChatName(chatId.Value, personRepose.User.Id, $"{userResponse.User.FirstName} {userResponse.User.LastName}");

                await _chatCache.SetChatImage(chatId.Value, senderId, personRepose.User.ProfilePicture);
                await _chatCache.SetChatImage(chatId.Value, personRepose.User.Id, userResponse.User.ProfilePicture);

                _metrics.Increment("chats_created_person");

                _logger.LogInformation(
                    "Личный чат {ChatId} создан между пользователями {UserId} и {TargetUserId}",
                    chatId.Value,
                    senderId,
                    personRepose.User.Id
                );
            }
            else
            {
                chatId = chatIdWithPerson;
                _logger.LogDebug(
                    "Используется существующий личный чат {ChatId}",
                    chatId.Value
                );
            }
        }

        List<Domain.MessageAttachment> attachments = new();
        Dictionary<string, UploadFileInfo> filesInfoMap = new();

        if (request.Message.FileIds != null && request.Message.FileIds.Any())
        {
            _logger.LogDebug(
                "Обработка {FileCount} вложений для сообщения",
                request.Message.FileIds.Count()
            );

            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest { FileIds = { request.Message.FileIds.Select(x => x.ToString()) } });

            if (filesInfo.FilesInfos.Any(x => !_attachmentMap.ContainsKey(x.Type)))
            {
                _logger.LogWarning(
                    "Обнаружены неподдерживаемые типы файлов в сообщении от пользователя {UserId}",
                    senderId
                );
                throw new FileNotSupportedException();
            }

            filesInfoMap = filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);

            attachments = filesInfo.FilesInfos.Select(x => new Domain.MessageAttachment
            {
                FileId = x.Id,
                FileSize = x.FileSize,
                PreviewUrl = x.PreviewUrl,
                Type = _attachmentMap[x.Type]
            }).ToList();

            _logger.LogDebug(
                "Добавлено {AttachmentCount} вложений к сообщению",
                attachments.Count
            );
        }

        if (request.Message.ForwardedMessageId.HasValue)
        {
            _logger.LogDebug(
                "Обработка пересланного сообщения {OriginalMessageId} от пользователя {UserId}",
                request.Message.ForwardedMessageId.Value,
                senderId
            );

            var originalMessages = await _messagesStorage.GetMessagesByIds([request.Message.ForwardedMessageId.Value]);
            var originalMessage = originalMessages.FirstOrDefault();

            if (originalMessage is null)
            {
                _logger.LogWarning(
                    "Оригинальное сообщение {MessageId} для пересылки не найдено",
                    request.Message.ForwardedMessageId.Value
                );
                throw new MessageNotFoundException();
            }

            var hasAccessToOriginal = await _chatsStorage.CheckAccessToChat(originalMessage.ChatId, senderId);
            if (!hasAccessToOriginal)
            {
                _logger.LogWarning(
                    "Пользователь {UserId} не имеет доступа к чату {ChatId} оригинального сообщения",
                    senderId,
                    originalMessage.ChatId
                );
                throw new NoAccessToChatException();
            }

            string authorName;
            if (originalMessage.SenderId is { } originalSenderId)
            {
                var authorResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = originalSenderId });
                authorName = $"{authorResponse.User.FirstName} {authorResponse.User.LastName}";
            }
            else if (originalMessage.SenderUuid.HasValue)
            {
                // fed-автор (SenderId = NULL) — профиль в RemoteUsers, явное имя для forward'а
                // в Фазе 5 (рендер). Здесь используем FID как заглушку, чтобы не падать.
                var remote = await _usersServerApiClient.GetUsersByUuidAsync(
                    new GetUsersByUuidRequest { Uuids = { originalMessage.SenderUuid.Value.ToString() } });
                var p = remote.Users.FirstOrDefault();
                authorName = p is { Found: true }
                    ? $"{p.FirstName} {p.LastName}".Trim()
                    : (p?.Username ?? string.Empty);
            }
            else
            {
                authorName = string.Empty;
            }

            var forwardedAttachments = originalMessage.Content?.Attachments?
                .Where(a => a.Type != Domain.MessageAttachmentType.ForwardedMessage)
                .Select(a => new Domain.ForwardedMessageAttachment
                {
                    Type = a.Type,
                    FileId = a.FileId ?? string.Empty,
                    PreviewUrl = a.PreviewUrl,
                    FileSize = a.FileSize
                })
                .ToList();

            // Загружаем метаданные файлов из вложений оригинального сообщения
            var forwardedFileIds = forwardedAttachments?
                .Where(fa => !string.IsNullOrEmpty(fa.FileId))
                .Select(fa => fa.FileId)
                .ToList();

            if (forwardedFileIds is { Count: > 0 })
            {
                var forwardedFilesInfo = await _filesServerApiClient.GetFilesDataAsync(
                    new GetFilesDataRequest { FileIds = { forwardedFileIds } });

                foreach (var fi in forwardedFilesInfo.FilesInfos)
                {
                    filesInfoMap.TryAdd(fi.Id, fi);
                }
            }

            attachments.Add(new Domain.MessageAttachment
            {
                Type = Domain.MessageAttachmentType.ForwardedMessage,
                FileId = string.Empty,
                ForwardedAuthorName = authorName,
                ForwardedOriginalMessageId = originalMessage.Id,
                ForwardedText = originalMessage.Content?.Text,
                ForwardedAttachments = forwardedAttachments
            });

            _metrics.Increment("messages_forwarded");

            _logger.LogInformation(
                "Добавлено пересланное сообщение {OriginalMessageId} от автора {AuthorName}",
                originalMessage.Id,
                authorName
            );
        }

        _logger.LogDebug("Получение списка участников чата {ChatId}", chatId.Value);

        var members = await _chatsStorage.GetChatMembers(chatId.Value, 0, int.MaxValue);

        // Отправка последующих сообщений в уже существующий fed-DM через chat_id (docs/rearch/05,
        // «Отправка последующих сообщений»): признак федеративности берём из состава участников —
        // remote-участник (UserId=NULL, ServerName задан) есть только у fed-чатов. Ветка выше
        // (request.UserUuid) покрывает только первое сообщение/явную адресацию по uuid.
        if (!isFederated)
        {
            var remoteMembers = members
                .Where(m => !string.IsNullOrEmpty(m.ServerName) && m.UserUuid.HasValue)
                .ToList();
            if (remoteMembers.Count > 0)
            {
                isFederated = true;
                senderUuid = members.FirstOrDefault(m => m.UserId == senderId)?.UserUuid ?? Guid.Empty;
                remoteParticipants = remoteMembers
                    .Select(m => new FederatedParticipant { Uuid = m.UserUuid!.Value, ServerName = m.ServerName! })
                    .ToList();

                // senderFid для этой ветки не резолвится выше (та ветка — только для нового fed-чата
                // через UserUuid) — без него удалённая нода получает пустой Sender.Username на каждое
                // сообщение после первого в чате.
                var selfResp = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = senderId });
                senderFid = $"@{selfResp.User.Username}:{remoteParticipants[0].ServerName}";
            }
        }

        var message = new Message
        {
            ChatId = chatId.Value,
            Content = new MessageContent()
            {
                Attachments = attachments,
                Text = request.Message.Text
            },
            ReadBy = [senderId],
            SenderId = senderId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.Generic
        };

        if (isFederated)
        {
            // FederatedId / SenderUuid / LastChangeAt для fed-сообщения.
            message.FederatedId = federatedId;
            message.SenderUuid = senderUuid;
        }

        _logger.LogDebug(
            "Сохранение сообщения в БД. Чат: {ChatId}, Участников: {MemberCount}",
            chatId.Value,
            members.Count
        );

        message = await _messagesStorage.AddMessage(message);

        _logger.LogInformation(
            "Сообщение {MessageId} сохранено в чат {ChatId}. Отправка в очередь для {MemberCount} участников",
            message.Id,
            chatId.Value,
            members.Count
        );

        if (isFederated)
        {
            await _messageQueueSender.SendFederatedMessage(
                message,
                chatId.Value,
                members.LocalUserIds(),
                filesInfoMap,
                federatedId,
                senderUuid,
                remoteParticipants,
                isFirstMessageInFedChat,
                fedInitiatorUuid,
                fedInviteeUuid,
                senderFid,
                lastChangeAt: message.LastChangeAt);
        }
        else
        {
            await _messageQueueSender.SendMessage(message, chatId.Value, members.LocalUserIds(), filesInfoMap);
        }

        _metrics.Increment("messages_sent");
        if (!string.IsNullOrEmpty(request.Message.Text))
            _metrics.Increment("messages_sent_with_text");
        if (attachments.Count > 0)
        {
            _metrics.Increment("messages_sent_with_attachments");
            _metrics.Add("attachments_total", attachments.Count);
        }
        _metrics.Set("last_message_sent_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _logger.LogInformation(
            "Сообщение {MessageId} успешно отправлено в чат {ChatId} от пользователя {UserId}",
            message.Id,
            chatId.Value,
            senderId
        );

        return new SendMessageResponse() { Message = message.ToGrpc(filesInfoMap) };
    }
}
