using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;
using ChatMember = BarkFluff.Messages.Domain.ChatMember;
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

    public CreateGroupChatCommandHandler(UserContext userContext, FilesServerApi.FilesServerApiClient filesServerApiClient, 
        ChatsStorage chatsStorage, MessagesStorage messagesStorage, MessageQueueSender messageQueueSender)
    {
        _userContext = userContext;
        _filesServerApiClient = filesServerApiClient;
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _messageQueueSender = messageQueueSender;
    }
    
    public async Task<CreateGroupChatResponse> Handle(CreateGroupChatCommand request, CancellationToken cancellationToken)
    {
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
            var fileInfo = await _filesServerApiClient.GetFileDataAsync(new GetFileDataRequest
                { FileId = request.PictureFileId.Value.ToString() });

            if (fileInfo.FileInfo.Type != UploadFileType.ChatPicture)
            {
                throw new FileHasNotGroupPictureTypeException();
            }

            pictureUrl = fileInfo.FileInfo.FileUrl;
        }
        
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

        await _messageQueueSender.SendMessage(message, groupChat.Id, request.UserIds);

        groupChat.LastMessage = message;
        groupChat.Members = [];
        
        return new CreateGroupChatResponse()
        {
            CreatedChat = groupChat.ToGrpc()
        };
    }
}