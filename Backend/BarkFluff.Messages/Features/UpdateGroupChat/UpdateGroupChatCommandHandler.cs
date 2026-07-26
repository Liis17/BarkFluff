using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

using Message = BarkFluff.Messages.Domain.Message;
using MessageContent = BarkFluff.Messages.Domain.MessageContent;
using MessageContentType = BarkFluff.Messages.Domain.MessageContentType;

namespace BarkFluff.Messages.Features.UpdateGroupChat;

using Infrastructure;

public class UpdateGroupChatCommandHandler : IRequestHandler<UpdateGroupChatCommand, UpdateGroupChatResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly MessagesStorage _messagesStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<UpdateGroupChatCommandHandler> _logger;

    public UpdateGroupChatCommandHandler(ChatsStorage chatsStorage, UserContext userContext, MessagesStorage messagesStorage,
        FilesServerApi.FilesServerApiClient filesServerApiClient, UsersServerApi.UsersServerApiClient usersServerApiClient,
        MessageQueueSender messageQueueSender, MetricsCollector metrics, ILogger<UpdateGroupChatCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _messagesStorage = messagesStorage;
        _filesServerApiClient = filesServerApiClient;
        _usersServerApiClient = usersServerApiClient;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<UpdateGroupChatResponse> Handle(UpdateGroupChatCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Изменение группового чата {ChatId} пользователем {UserId}",
            request.ChatId,
            _userContext.UserId
        );

        if (request.Title is null && request.PictureFileId is null)
        {
            // Нечего менять — возвращаем текущее состояние.
            var current = await _chatsStorage.GetChat(request.ChatId);
            if (current == null)
            {
                throw new NoAccessToChatException();
            }

            return new UpdateGroupChatResponse { Chat = current.ToGrpc() };
        }

        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var chatInfo = await _chatsStorage.GetChat(request.ChatId);

        if (chatInfo == null)
        {
            throw new NoAccessToChatException();
        }

        if (!chatInfo.IsGroupChat)
        {
            throw new IsNotGroupChatException();
        }

        var groupChatInfo = await _chatsStorage.GetGroupChatInfo(request.ChatId);

        if (groupChatInfo == null)
        {
            _logger.LogWarning("Информация о групповом чате {ChatId} не найдена", request.ChatId);
            throw new NoAccessToChatException();
        }

        if (!groupChatInfo.UsersCanKick.Contains(_userContext.UserId))
        {
            _logger.LogWarning(
                "Пользователь {UserId} не имеет прав на изменение чата {ChatId}",
                _userContext.UserId,
                request.ChatId
            );
            throw new NoPermissionException();
        }

        string? newTitle = null;

        if (request.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new GroupChatTitleIsEmptyException();
            }

            newTitle = request.Title;
        }

        string? newPictureUrl = null;

        if (request.PictureFileId is not null)
        {
            _logger.LogDebug("Получение информации о новом файле обложки {FileId}", request.PictureFileId);

            var fileInfo = await _filesServerApiClient.GetFileDataAsync(new GetFileDataRequest
            { FileId = request.PictureFileId.Value.ToString() });

            if (fileInfo.FileInfo.Type != UploadFileType.ChatPicture)
            {
                _logger.LogWarning(
                    "Файл {FileId} имеет неверный тип {FileType}, ожидается {ExpectedType}",
                    request.PictureFileId,
                    fileInfo.FileInfo.Type,
                    UploadFileType.ChatPicture
                );
                throw new FileHasNotGroupPictureTypeException();
            }

            newPictureUrl = fileInfo.FileInfo.FileUrl;
        }

        await _chatsStorage.UpdateGroupChat(request.ChatId, newTitle, newPictureUrl);

        var editorInfoResponse = await
            _usersServerApiClient.GetByIdAsync(new GetByIdRequest() { UserId = _userContext.UserId });

        var editorName = $"{editorInfoResponse.User.FirstName} {editorInfoResponse.User.LastName}";

        string systemText;
        if (newTitle is not null && newPictureUrl is not null)
        {
            systemText = $"Пользователь {editorName} изменил название и аватар группового чата";
        }
        else if (newTitle is not null)
        {
            systemText = $"Пользователь {editorName} изменил название группового чата на \"{newTitle}\"";
        }
        else
        {
            systemText = $"Пользователь {editorName} изменил аватар группового чата";
        }

        var systemMessage = new Message()
        {
            ChatId = request.ChatId,
            Content = new MessageContent() { Text = systemText },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };

        systemMessage = await _messagesStorage.AddMessage(systemMessage);

        _logger.LogDebug("Отправка системного сообщения об изменении чата");

        await _messageQueueSender.SendMessage(systemMessage, request.ChatId, chatInfo.Members!.LocalUserIds());

        _metrics.Increment("group_chats_updated");

        chatInfo.Title = newTitle ?? chatInfo.Title;
        chatInfo.Picture = newPictureUrl ?? chatInfo.Picture;
        chatInfo.LastMessage = systemMessage;
        chatInfo.Members = [];

        _logger.LogInformation(
            "Групповой чат {ChatId} успешно изменён пользователем {UserId}",
            request.ChatId,
            _userContext.UserId
        );

        return new UpdateGroupChatResponse { Chat = chatInfo.ToGrpc() };
    }
}
