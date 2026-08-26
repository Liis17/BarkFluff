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
    private const int MaxForwardedMessages = 20;

    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UserContext _userContext;
    private readonly ChatCache _chatCache;
    private readonly MessagesStorage _messagesStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ReplyPreviewResolver _replyPreviewResolver;
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
        MessageQueueSender messageQueueSender, IConfiguration configuration, MetricsCollector metrics,
        ReplyPreviewResolver replyPreviewResolver, ILogger<SendMessageCommandHandler> logger)
    {
        _replyPreviewResolver = replyPreviewResolver;
        _chatsStorage = chatsStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _filesServerApiClient = filesServerApiClient;
        _chatCache = chatCache;
        _messagesStorage = messagesStorage;
        _messageQueueSender = messageQueueSender;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SendMessageResponse> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        // Серверный путь (SendMessageServer от сервиса Bots) передаёт отправителя явно, клиентский — из токена.
        var senderId = request.SenderId ?? _userContext.UserId;

        if (request.ClientOperationId is { } clientOperationId)
        {
            var existing = await _messagesStorage.GetByClientOperationIdAsync(
                senderId,
                clientOperationId,
                cancellationToken);
            if (existing is not null)
                return new SendMessageResponse { Message = existing.ToGrpc() };
        }

        _logger.LogInformation(
            "Начало отправки сообщения от пользователя {UserId} в чат {ChatId} или пользователю {TargetUserId}",
            senderId,
            request.ChatId,
            request.UserId
        );

        // Ответ содержимым не является: reply без текста и вложений — пустое сообщение.
        // Пересылка содержимым является, поэтому учитывается наравне с текстом и файлами.
        if (request.Message is null ||
            request.Message.Text is null &&
            request.Message.FileIds is null &&
            request.Message.ForwardedMessageIds is null)
        {
            _logger.LogWarning(
                "Попытка отправки пустого сообщения от пользователя {UserId}",
                senderId
            );
            throw new MessageNotContainContextException();
        }

        if (request.Message.ForwardedMessageIds is { Count: > MaxForwardedMessages } forwardedIds)
        {
            _logger.LogWarning(
                "Сообщение от пользователя {UserId} пересылает {ForwardCount} сообщений (лимит: {MaxForwarded})",
                senderId,
                forwardedIds.Count,
                MaxForwardedMessages
            );
            throw new TooManyForwardedMessagesException();
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
                new GetUsersByUuidRequest { Uuids = { targetUuid.ToString() } },
                cancellationToken: cancellationToken);
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
                var selfResp = await _usersServerApiClient.GetByIdAsync(
                    new GetByIdRequest { UserId = senderId },
                    cancellationToken: cancellationToken);
                if (!Guid.TryParse(selfResp.User.Uuid, out senderUuid) || senderUuid == Guid.Empty)
                    throw new RemoteUserNotResolvedException();

                senderFid = $"@{selfResp.User.Username}:{target.ServerName}";

                var (uuidLow, uuidHigh) = Features.Federation.FederatedUuidPair.Normalize(senderUuid, targetUuid);

                var existing = await _chatsStorage.FindActiveFederatedChatByUuidPairAsync(
                    uuidLow,
                    uuidHigh,
                    cancellationToken);
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
                        uuidHigh,
                        cancellationToken);
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

            var hasAccess = await _chatsStorage.CheckAccessToChat(
                chatId.Value,
                senderId,
                cancellationToken);

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
            var federatedStatus = await _chatsStorage.GetFederatedStatusAsync(
                chatId.Value,
                cancellationToken);
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
            var personRepose = await _usersServerApiClient.GetByIdAsync(
                new GetByIdRequest { UserId = request.UserId!.Value },
                cancellationToken: cancellationToken);

            var chatIdWithPerson = await _chatsStorage.GetUserChatIdWithPerson(
                personRepose.User.Id,
                senderId,
                cancellationToken);

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

                var createdChat = await _chatsStorage.CreatePersonChat(
                    senderId,
                    personRepose.User.Id,
                    cancellationToken);

                chatId = createdChat.Id;

                var userResponse = await _usersServerApiClient.GetByIdAsync(
                    new GetByIdRequest { UserId = senderId },
                    cancellationToken: cancellationToken);

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

        // Отвечать можно только на сообщение этого же чата — то же правило, что при сохранении
        // черновика. Проверка идёт после того, как chatId окончательно определён (личный чат мог
        // быть создан выше), иначе reply в новый чат сравнивался бы с пустым chatId.
        if (request.Message.ReplyToMessageId is { } replyToMessageId)
        {
            await Features.Shared.ReplyTargetValidator.ValidateAsync(
                _messagesStorage,
                chatId.Value,
                replyToMessageId,
                cancellationToken);

            _logger.LogDebug(
                "Сообщение является ответом на {ReplyToMessageId} в чате {ChatId}",
                replyToMessageId,
                chatId.Value
            );
        }

        List<Domain.MessageAttachment> attachments = new();
        Dictionary<string, UploadFileInfo> filesInfoMap = new();

        if (request.Message.FileIds != null && request.Message.FileIds.Any())
        {
            _logger.LogDebug(
                "Обработка {FileCount} вложений для сообщения",
                request.Message.FileIds.Count()
            );

            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(
                new GetFilesDataRequest { FileIds = { request.Message.FileIds.Select(x => x.ToString()) } },
                cancellationToken: cancellationToken);

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
                FileName = x.FileName,
                PreviewUrl = x.PreviewUrl,
                Type = _attachmentMap[x.Type]
            }).ToList();

            _logger.LogDebug(
                "Добавлено {AttachmentCount} вложений к сообщению",
                attachments.Count
            );
        }

        if (request.Message.ForwardedMessageIds is { Count: > 0 } forwardSourceIds)
        {
            _logger.LogDebug(
                "Обработка {ForwardCount} пересланных сообщений от пользователя {UserId}",
                forwardSourceIds.Count,
                senderId
            );

            var originalMessages = await _messagesStorage.GetMessagesByIds(
                forwardSourceIds,
                cancellationToken);
            var originalsById = originalMessages.ToDictionary(m => m.Id);

            if (forwardSourceIds.Any(id => !originalsById.ContainsKey(id)))
            {
                _logger.LogWarning(
                    "Часть оригинальных сообщений для пересылки не найдена (запрошено {RequestedCount}, найдено {FoundCount})",
                    forwardSourceIds.Count,
                    originalsById.Count
                );
                throw new MessageNotFoundException();
            }

            // Доступ проверяем по уникальным чатам, а не по каждому сообщению: пересылка пачки из
            // одного чата иначе давала бы N одинаковых запросов.
            foreach (var sourceChatId in originalMessages.Select(m => m.ChatId).Distinct())
            {
                if (!await _chatsStorage.CheckAccessToChat(sourceChatId, senderId, cancellationToken))
                {
                    _logger.LogWarning(
                        "Пользователь {UserId} не имеет доступа к чату {ChatId} оригинального сообщения",
                        senderId,
                        sourceChatId
                    );
                    throw new NoAccessToChatException();
                }
            }

            var authorNames = await ResolveForwardAuthorNamesAsync(originalMessages, cancellationToken);

            // Один GetFilesData на вложения всех пересылаемых сообщений разом.
            var forwardedAttachmentsBySource = new Dictionary<long, List<Domain.ForwardedMessageAttachment>>();
            var allForwardedFileIds = new List<string>();

            foreach (var original in originalMessages)
            {
                var forwardedAttachments = original.Content?.Attachments?
                    .Where(a => a.Type != Domain.MessageAttachmentType.ForwardedMessage)
                    .Select(a => new Domain.ForwardedMessageAttachment
                    {
                        Type = a.Type,
                        FileId = a.FileId ?? string.Empty,
                        PreviewUrl = a.PreviewUrl,
                        FileSize = a.FileSize,
                        // Форвард fed-вложения сохраняет origin: иначе проверка доступа (3.3)
                        // не сможет точно сопоставить файл с нодой-владельцем.
                        OriginServer = a.OriginServer
                    })
                    .ToList();

                if (forwardedAttachments is not null)
                {
                    forwardedAttachmentsBySource[original.Id] = forwardedAttachments;
                    allForwardedFileIds.AddRange(forwardedAttachments
                        .Where(fa => !string.IsNullOrEmpty(fa.FileId))
                        .Select(fa => fa.FileId));
                }
            }

            if (allForwardedFileIds.Count > 0)
            {
                var forwardedFilesInfo = await _filesServerApiClient.GetFilesDataAsync(
                    new GetFilesDataRequest { FileIds = { allForwardedFileIds.Distinct() } },
                    cancellationToken: cancellationToken);

                foreach (var fi in forwardedFilesInfo.FilesInfos)
                {
                    filesInfoMap.TryAdd(fi.Id, fi);
                }
            }

            // Порядок задаёт клиент, а не выдача БД: ForwardedOrder — то, в каком виде пользователь
            // видел пересылку при отправке.
            for (var order = 0; order < forwardSourceIds.Count; order++)
            {
                var original = originalsById[forwardSourceIds[order]];

                attachments.Add(new Domain.MessageAttachment
                {
                    Type = Domain.MessageAttachmentType.ForwardedMessage,
                    FileId = string.Empty,
                    ForwardedAuthorName = authorNames.GetValueOrDefault(original.Id, string.Empty),
                    ForwardedOriginalMessageId = original.Id,
                    ForwardedText = original.Content?.Text,
                    ForwardedAttachments = forwardedAttachmentsBySource.GetValueOrDefault(original.Id),
                    ForwardedOriginalChatId = original.ChatId,
                    ForwardedOriginalSenderId = original.SenderId,
                    ForwardedOriginalSentAt = original.SentAt,
                    ForwardedOrder = order
                });
            }

            _metrics.Add("messages_forwarded", forwardSourceIds.Count);

            _logger.LogInformation(
                "Добавлено {ForwardCount} пересланных сообщений",
                forwardSourceIds.Count
            );
        }

        _logger.LogDebug("Получение списка участников чата {ChatId}", chatId.Value);

        var members = await _chatsStorage.GetChatMembers(
            chatId.Value,
            0,
            int.MaxValue,
            cancellationToken);

        // Отправка последующих сообщений в уже существующий fed-DM через chat_id (docs/rearch/05,
        // «Отправка последующих сообщений»): признак федеративности берём из состава участников —
        // remote-участник (UserId=NULL, ServerName задан) есть только у fed-чатов. Ветка выше
        // (request.UserUuid) покрывает только первое сообщение/явную адресацию по uuid.
        if (!isFederated)
        {
            var remoteMembers = members.RemoteParticipants();
            if (remoteMembers.Count > 0)
            {
                isFederated = true;
                senderUuid = members.FirstOrDefault(m => m.UserId == senderId)?.UserUuid ?? Guid.Empty;
                remoteParticipants = remoteMembers;

                // senderFid для этой ветки не резолвится выше (та ветка — только для нового fed-чата
                // через UserUuid) — без него удалённая нода получает пустой Sender.Username на каждое
                // сообщение после первого в чате.
                var selfResp = await _usersServerApiClient.GetByIdAsync(
                    new GetByIdRequest { UserId = senderId },
                    cancellationToken: cancellationToken);
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
            ClientOperationId = request.ClientOperationId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.Generic,
            ReplyToMessageId = request.Message.ReplyToMessageId
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

        Guid? replyToFederatedMessageId = null;
        if (isFederated && message.ReplyToMessageId is { } replyTargetId)
        {
            var replyTarget = await _messagesStorage.GetMessageById(replyTargetId, cancellationToken);
            replyToFederatedMessageId = replyTarget?.FederatedId;
        }

        Func<Message, BarkFluff.Shared.Queue.Messages.NewMessageEvent> eventFactory;
        if (isFederated)
        {
            eventFactory = savedMessage => _messageQueueSender.CreateFederatedMessageEvent(
                savedMessage,
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
                lastChangeAt: savedMessage.LastChangeAt,
                federatedAttachments: Features.Federation.FederatedAttachmentMapper.Build(
                    savedMessage.Content?.Attachments,
                    filesInfoMap,
                    _configuration["Federation:ServerName"] ?? string.Empty),
                replyToFederatedMessageId: replyToFederatedMessageId);
        }
        else
        {
            eventFactory = savedMessage => _messageQueueSender.CreateMessageEvent(
                savedMessage,
                chatId.Value,
                members.LocalUserIds(),
                filesInfoMap);
        }

        var saveResult = await _messagesStorage.AddMessageWithOutboxAsync(
            message,
            eventFactory,
            cancellationToken);
        message = saveResult.Message;

        _logger.LogInformation(
            "Сообщение {MessageId} сохранено в чат {ChatId} и поставлено в outbox для {MemberCount} участников",
            message.Id,
            chatId.Value,
            members.Count
        );

        if (!saveResult.Created)
            return new SendMessageResponse { Message = message.ToGrpc(filesInfoMap) };

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

        // Отправитель должен увидеть свою цитату сразу, не дожидаясь перезагрузки истории.
        var replyPreviews = await _replyPreviewResolver.ResolveAsync([message], cancellationToken);

        return new SendMessageResponse() { Message = message.ToGrpc(filesInfoMap, replyPreviews: replyPreviews) };
    }

    /// <summary>
    /// Имена авторов пересылаемых сообщений: два батч-вызова (локальные и remote) вместо запроса
    /// на каждое сообщение. До пересылки пачками разница была незаметна — с ней это был бы N+1.
    /// </summary>
    private async Task<Dictionary<long, string>> ResolveForwardAuthorNamesAsync(
        List<Message> originals,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<long, string>();

        var localSenderIds = originals
            .Where(m => m.SenderId.HasValue)
            .Select(m => m.SenderId!.Value)
            .Distinct()
            .ToList();

        var localNamesById = new Dictionary<long, string>();
        if (localSenderIds.Count > 0)
        {
            var usersResponse = await _usersServerApiClient.ListByIdsAsync(
                new ListByIdsRequest { Ids = { localSenderIds } },
                cancellationToken: cancellationToken);

            foreach (var user in usersResponse.Users)
                localNamesById[user.Id] = $"{user.FirstName} {user.LastName}";
        }

        // fed-авторы (SenderId = NULL) живут в RemoteUsers — отдельный батч по UUID.
        var remoteUuids = originals
            .Where(m => m.SenderId is null && m.SenderUuid.HasValue)
            .Select(m => m.SenderUuid!.Value.ToString())
            .Distinct()
            .ToList();

        var remoteNamesByUuid = new Dictionary<string, string>();
        if (remoteUuids.Count > 0)
        {
            var remoteResponse = await _usersServerApiClient.GetUsersByUuidAsync(
                new GetUsersByUuidRequest { Uuids = { remoteUuids } },
                cancellationToken: cancellationToken);

            foreach (var profile in remoteResponse.Users)
            {
                remoteNamesByUuid[profile.Uuid] = profile.Found
                    ? $"{profile.FirstName} {profile.LastName}".Trim()
                    : profile.Username ?? string.Empty;
            }
        }

        foreach (var original in originals)
        {
            names[original.Id] = original.SenderId is { } senderId
                ? localNamesById.GetValueOrDefault(senderId, string.Empty)
                : original.SenderUuid is { } uuid
                    ? remoteNamesByUuid.GetValueOrDefault(uuid.ToString(), string.Empty)
                    : string.Empty;
        }

        return names;
    }
}
