using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;
using Microsoft.Extensions.Logging;
using Message = BarkFluff.Messages.Domain.Message;
using MessageAttachment = BarkFluff.Messages.Domain.MessageAttachment;
using MessageContent = BarkFluff.Messages.Domain.MessageContent;
using MessageContentType = BarkFluff.Messages.Domain.MessageContentType;

namespace BarkFluff.Messages.Features.SendMessage;

using Infrastructure;

public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, SendMessageResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UserContext _userContext;
    private readonly ChatCache _chatCache;
    private readonly MessagesStorage _messagesStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    private readonly Dictionary<UploadFileType, Domain.MessageAttachmentType> _attachmentMap =
        new()
        {
            { UploadFileType.MessageAttachmentImage, Domain.MessageAttachmentType.Image },
            { UploadFileType.MessageAttachmentDocument, Domain.MessageAttachmentType.Document },
            { UploadFileType.MessageAttachmentGif, Domain.MessageAttachmentType.Gif },
            { UploadFileType.MessageAttachmentVideo, Domain.MessageAttachmentType.Video },
            { UploadFileType.MessageAttachmentAudio, Domain.MessageAttachmentType.Audio },
            { UploadFileType.MessageAttachmentVoice, Domain.MessageAttachmentType.Voice }
        };

    public SendMessageCommandHandler(ChatsStorage chatsStorage, UsersServerApi.UsersServerApiClient usersServerApiClient,
        UserContext userContext, FilesServerApi.FilesServerApiClient filesServerApiClient, ChatCache chatCache, MessagesStorage messagesStorage,
        MessageQueueSender messageQueueSender, ILogger<SendMessageCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _filesServerApiClient = filesServerApiClient;
        _chatCache = chatCache;
        _messagesStorage = messagesStorage;
        _messageQueueSender = messageQueueSender;
        _logger = logger;
    }

    public async Task<SendMessageResponse> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало отправки сообщения от пользователя {UserId} в чат {ChatId} или пользователю {TargetUserId}",
            _userContext.UserId,
            request.ChatId,
            request.UserId
        );

        if (request.Message is null || request.Message.Text is null && request.Message.FileIds is null)
        {
            _logger.LogWarning(
                "Попытка отправки пустого сообщения от пользователя {UserId}",
                _userContext.UserId
            );
            throw new MessageNotContainContextException();
        }

        var chatId = request.ChatId;

        if (chatId is null && request.UserId is null)
        {
            _logger.LogWarning(
                "Не указан ни ChatId, ни UserId для отправки сообщения от пользователя {UserId}",
                _userContext.UserId
            );
            throw new SourceForSendMessageNotSetException();
        }

        if (chatId != null)
        {
            _logger.LogDebug("Проверка доступа пользователя {UserId} к чату {ChatId}", _userContext.UserId, chatId.Value);

            var hasAccess = await _chatsStorage.CheckAccessToChat(chatId.Value, _userContext.UserId);

            if(!hasAccess)
            {
                _logger.LogWarning(
                    "Пользователь {UserId} не имеет доступа к чату {ChatId}",
                    _userContext.UserId,
                    chatId.Value
                );
                throw new NoAccessToChatException();
            }
        }

        if (chatId is null)
        {
            _logger.LogDebug(
                "Создание или получение личного чата между пользователями {UserId} и {TargetUserId}",
                _userContext.UserId,
                request.UserId
            );

            // Получаем пользователя по ID
            var personRepose = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId!.Value });

            var chatIdWithPerson = await _chatsStorage.GetUserChatIdWithPerson(personRepose.User.Id, _userContext.UserId);

            if (chatIdWithPerson is null)
            {
                _logger.LogInformation(
                    "Создание нового личного чата между пользователями {UserId} и {TargetUserId}",
                    _userContext.UserId,
                    personRepose.User.Id
                );

                var createdChat = await _chatsStorage.CreatePersonChat(_userContext.UserId, personRepose.User.Id);

                chatId = createdChat.Id;

                var userResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });

                // Кэшируем аватарочки и имена
                await _chatCache.SetChatName(chatId.Value, _userContext.UserId, $"{personRepose.User.FirstName} {personRepose.User.LastName}");
                await _chatCache.SetChatName(chatId.Value, personRepose.User.Id, $"{userResponse.User.FirstName} {userResponse.User.LastName}");

                await _chatCache.SetChatImage(chatId.Value, _userContext.UserId, personRepose.User.ProfilePicture);
                await _chatCache.SetChatImage(chatId.Value, personRepose.User.Id, userResponse.User.ProfilePicture);

                _logger.LogInformation(
                    "Личный чат {ChatId} создан между пользователями {UserId} и {TargetUserId}",
                    chatId.Value,
                    _userContext.UserId,
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

        List<Domain.MessageAttachment>? attachments = new List<MessageAttachment>();
        Dictionary<string, UploadFileInfo> filesInfoMap = new();

        if (request.Message.FileIds != null && request.Message.FileIds.Any())
        {
            _logger.LogDebug(
                "Обработка {FileCount} вложений для сообщения",
                request.Message.FileIds.Count()
            );

            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest { FileIds = { request.Message.FileIds.Select(x => x.ToString())}});

            if (filesInfo.FilesInfos.Any(x => !_attachmentMap.ContainsKey(x.Type)))
            {
                _logger.LogWarning(
                    "Обнаружены неподдерживаемые типы файлов в сообщении от пользователя {UserId}",
                    _userContext.UserId
                );
                throw new FileNotSupportedException();
            }

            filesInfoMap = filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);

            attachments = filesInfo.FilesInfos.Select(x => new Domain.MessageAttachment { FileId = x.Id,
                FileSize = x.FileSize,
                PreviewUrl = x.PreviewUrl,
                Type = _attachmentMap[x.Type]}).ToList();

            _logger.LogDebug(
                "Добавлено {AttachmentCount} вложений к сообщению",
                attachments.Count
            );

            if (!attachments.Any())
            {
                attachments = new List<MessageAttachment>();
            }
        }

        var message = new Message
        {
            ChatId = chatId.Value, Content = new MessageContent()
            {
                Attachments = attachments, Text = request.Message.Text
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.Generic
        };

        _logger.LogDebug("Получение списка участников чата {ChatId}", chatId.Value);

        var members = await _chatsStorage.GetChatMembers(chatId.Value, 0, int.MaxValue);

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

        await _messageQueueSender.SendMessage(message, chatId.Value, members
            .Select(x => x.UserId).ToList());

        _logger.LogInformation(
            "Сообщение {MessageId} успешно отправлено в чат {ChatId} от пользователя {UserId}",
            message.Id,
            chatId.Value,
            _userContext.UserId
        );

        return new SendMessageResponse() { Message = message.ToGrpc(filesInfoMap) };
    }
}