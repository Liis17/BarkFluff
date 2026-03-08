using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

using Message = BarkFluff.Messages.Domain.Message;
using MessageContent = BarkFluff.Messages.Domain.MessageContent;
using MessageContentType = BarkFluff.Messages.Domain.MessageContentType;

namespace BarkFluff.Messages.Features.CreateGroupChat;

using Infrastructure;

public class CreateGroupChatCommandHandler : IRequestHandler<CreateGroupChatCommand, CreateGroupChatResponse>
{
    private readonly UserContext _userContext;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly ChatsStorage _chatsStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly ILogger<CreateGroupChatCommandHandler> _logger;

    public CreateGroupChatCommandHandler(UserContext userContext, FilesServerApi.FilesServerApiClient filesServerApiClient,
        ChatsStorage chatsStorage, MessagesStorage messagesStorage, MessageQueueSender messageQueueSender,
        ILogger<CreateGroupChatCommandHandler> logger)
    {
        _userContext = userContext;
        _filesServerApiClient = filesServerApiClient;
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _messageQueueSender = messageQueueSender;
        _logger = logger;
    }

    public async Task<CreateGroupChatResponse> Handle(CreateGroupChatCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Создание группового чата '{Title}' пользователем {UserId}, участников: {MemberCount}",
            request.Title,
            _userContext.UserId,
            request.UserIds.Count()
        );
        if (string.IsNullOrEmpty(request.Title))
        {
            throw new GroupChatTitleIsEmptyException();
        }

        if (!request.UserIds.Any())
        {
            throw new GroupChatUsersIsEmptyException();
        }

        if (!request.UserIds.Contains(_userContext.UserId))
        {
            request.UserIds.Add(_userContext.UserId);
        }

        string? pictureUrl = null;

        if (request.PictureFileId != null)
        {
            _logger.LogDebug("Получение информации о файле картинки группового чата {FileId}", request.PictureFileId);

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

            pictureUrl = fileInfo.FileInfo.FileUrl;
        }

        _logger.LogDebug("Создание группового чата в БД");

        var groupChat = await _chatsStorage.CreateGroupChat(request.UserIds, request.Title, pictureUrl);

        var groupChatInfo = new GroupChatInfo()
        {
            ChatId = groupChat.Id,
            Creator = _userContext.UserId,
            CreatedAt = DateTime.UtcNow,
            UsersCanKick = [_userContext.UserId],
        };

        await _chatsStorage.CreateGroupChatInfo(groupChatInfo);

        var message = new Message()
        {
            ChatId = groupChat.Id,
            Content = new MessageContent()
            {
                Text = $"Создан групповой чат \" {request.Title} \"",
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };

        await _messagesStorage.AddMessage(message);

        _logger.LogDebug(
            "Отправка системного сообщения о создании чата в очередь для {MemberCount} участников",
            request.UserIds.Count()
        );

        await _messageQueueSender.SendMessage(message, groupChat.Id, request.UserIds);

        groupChat.LastMessage = message;
        groupChat.Members = [];

        _logger.LogInformation(
            "Групповой чат '{Title}' успешно создан. ChatId: {ChatId}, создатель: {CreatorId}, участников: {MemberCount}",
            request.Title,
            groupChat.Id,
            _userContext.UserId,
            request.UserIds.Count()
        );

        return new CreateGroupChatResponse()
        {
            CreatedChat = groupChat.ToGrpc()
        };
    }
}