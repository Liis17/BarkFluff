using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;
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

    private readonly Dictionary<UploadFileType, Domain.MessageAttachmentType> _attachmentMap =
        new()
        {
            { UploadFileType.MessageAttachmentImage, Domain.MessageAttachmentType.Image },
            { UploadFileType.MessageAttachmentDocument, Domain.MessageAttachmentType.Document },
            { UploadFileType.MessageAttachmentGif, Domain.MessageAttachmentType.Gif },
            { UploadFileType.MessageAttachmentVideo, Domain.MessageAttachmentType.Video }
        };

    public SendMessageCommandHandler(ChatsStorage chatsStorage, UsersServerApi.UsersServerApiClient usersServerApiClient,
        UserContext userContext, FilesServerApi.FilesServerApiClient filesServerApiClient, ChatCache chatCache, MessagesStorage messagesStorage, 
        MessageQueueSender messageQueueSender)
    {
        _chatsStorage = chatsStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _filesServerApiClient = filesServerApiClient;
        _chatCache = chatCache;
        _messagesStorage = messagesStorage;
        _messageQueueSender = messageQueueSender;
    }

    public async Task<SendMessageResponse> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        if (request.Message is null || request.Message.Text is null && request.Message.FileIds is null)
        {
            throw new MessageNotContainContextException();
        }
        
        var chatId = request.ChatId;

        if (chatId is null && request.UserId is null)
        {
            throw new SourceForSendMessageNotSetException();
        }

        if (chatId != null)
        {
            var hasAccess = await _chatsStorage.CheckAccessToChat(chatId.Value, _userContext.UserId);
            
            if(!hasAccess)
            {
                throw new NoAccessToChatException();
            }
        }

        if (chatId is null)
        {
            // Получаем пользователя по ID
            var personRepose = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId!.Value });
            
            var chatIdWithPerson = await _chatsStorage.GetUserChatIdWithPerson(personRepose.User.Id, _userContext.UserId);

            if (chatIdWithPerson is null)
            {
                var createdChat = await _chatsStorage.CreatePersonChat(_userContext.UserId, personRepose.User.Id);
                
                chatId = createdChat.Id;
                
                var userResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });

                // Кэшируем аватарочки и имена
                await _chatCache.SetChatName(chatId.Value, _userContext.UserId, $"{personRepose.User.FirstName} {personRepose.User.LastName}");
                await _chatCache.SetChatName(chatId.Value, personRepose.User.Id, $"{userResponse.User.FirstName} {userResponse.User.LastName}");
            
                await _chatCache.SetChatImage(chatId.Value, _userContext.UserId, personRepose.User.ProfilePicture);
                await _chatCache.SetChatImage(chatId.Value, personRepose.User.Id, userResponse.User.ProfilePicture);
            }
            else
            {
                chatId = chatIdWithPerson;
            }
        }

        List<Domain.MessageAttachment>? attachments = new List<MessageAttachment>();
        
        if (request.Message.FileIds != null && request.Message.FileIds.Any())
        {
            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest { FileIds = { request.Message.FileIds.Select(x => x.ToString())}});

            if (filesInfo.FilesInfos.Any(x => !_attachmentMap.ContainsKey(x.Type)))
            {
                throw new FileNotSupportedException();
            }
            
            attachments = filesInfo.FilesInfos.Select(x => new Domain.MessageAttachment { FileId = x.Id, 
                FileSize = x.FileSize, 
                PreviewUrl = x.PreviewUrl,
                Type = _attachmentMap[x.Type]}).ToList();

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
        
        var members = await _chatsStorage.GetChatMembers(chatId.Value, 0, int.MaxValue);

        message = await _messagesStorage.AddMessage(message);

        await _messageQueueSender.SendMessage(message, chatId.Value, members
            .Select(x => x.UserId).ToList());

        return new SendMessageResponse() { Message = message.ToGrpc() };
    }
}