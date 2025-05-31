using BarkFluff.Messages.Features.CreateGroupChat;
using BarkFluff.Messages.Features.KickUser;
using BarkFluff.Messages.Features.ListChatMembers;
using BarkFluff.Messages.Features.ListChats;
using BarkFluff.Messages.Features.ListMessages;
using BarkFluff.Messages.Features.MarkAsRead;
using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Exceptions.Files;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using OutgoingMessage = BarkFluff.Messages.Features.SendMessage.OutgoingMessage;

namespace BarkFluff.Messages.Host;

[Authorize(Policy = nameof(TokenType.User))]
public class MessagesApiService : BarkFluff.Proto.Messages.MessagesApi.MessagesApiBase
{
    private readonly IMediator _mediator;

    public MessagesApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<ListChatsResponse> ListChats(ListChatsRequest request, ServerCallContext context)
    {

        request.Pagination ??= new PageRequest()
        {
            Size = 10,
            Skip = 0
        };
        
        var command = new ListChatsCommand()
        {
            Size = request.Pagination.Size,
            Skip = request.Pagination.Skip,
        };
        
        return await _mediator.Send(command);
    }

    public override async Task<ListMessagesResponse> ListMessages(ListMessagesRequest request, ServerCallContext context)
    {
        var parseGuidResult = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!parseGuidResult)
        {
            throw new ChatIdNotValidException();
        }

        var command = new ListMessagesCommand
        {
            ChatId = chatId,
            Count = request.Count,
            FromMessageId = request.FromMessageId,
        };

        return await _mediator.Send(command);
    }

    public override async Task<ListChatMembersResponse> ListChatMembers(ListChatMembersRequest request, ServerCallContext context)
    {
        var parseGuidResult = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!parseGuidResult)
        {
            throw new ChatIdNotValidException();
        }

        var command = new ListChatMembersCommand()
        {
            ChatId = chatId,
            Count = request.Pagination.Size,
            Skip = request.Pagination.Skip,
        };
        
        return await _mediator.Send(command);
    }

    public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
    {
        if (request.Message is null)
        {
            throw new MessageNotContainContextException();
        }
        
        var command = new SendMessageCommand()
        {
            Message = new OutgoingMessage
            {
                FileIds = request.Message.FilesIds?.Select(x => Guid.Parse(x)).ToList(),
                Text = request.Message.Text
            },
        };

        switch (request.SourceIdCase)
        {
            case SendMessageRequest.SourceIdOneofCase.ChatId when Guid.TryParse(request.ChatId, out Guid chatId):
                command.ChatId = chatId;
                break;
            case SendMessageRequest.SourceIdOneofCase.ChatId:
                throw new ChatIdNotValidException();
            case SendMessageRequest.SourceIdOneofCase.UserId:
                command.UserId = request.UserId;
                break;
        }

        return await _mediator.Send(command);
    }

    public override async Task<CreateGroupChatResponse> CreateGroupChat(CreateGroupChatRequest request, ServerCallContext context)
    {
        Guid? pictureFileId = null;

        if (!string.IsNullOrEmpty(request.PictureFileId))
        {
            var hasValidGuid = Guid.TryParse(request.PictureFileId, out Guid pictureFileIdTmp);

            if (!hasValidGuid)
            {
                throw new NotValidFileIdException();
            }

            pictureFileId = pictureFileIdTmp;
        }

        var command = new CreateGroupChatCommand()
        {
            Title = request.Title,
            UserIds = request.UserIds.ToList(),
            PictureFileId = pictureFileId
        };
        
        return await _mediator.Send(command);
    }

    public override async Task<KickUserResponse> KickUser(KickUserRequest request, ServerCallContext context)
    {
       var hasValidGuid = Guid.TryParse(request.ChatId, out Guid chatId);

       if (!hasValidGuid)
       {
           throw new ChatIdNotValidException();
       }

       var command = new KickUserCommand()
       {
           UserId = request.UserId,
           ChatId = chatId
       };

       await _mediator.Send(command);

       return new KickUserResponse();
    }

    public override async Task<MarkAsReadResponse> MarkAsRead(MarkAsReadRequest request, ServerCallContext context)
    {
        var command = new MarkAsReadCommand
        {
            MessageIds = request.MessageIds.ToList()
        };

        await _mediator.Send(command);

        return new MarkAsReadResponse();
    }
}