using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Exceptions;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.ListMessages;

public class ListMessagesCommandHandler : IRequestHandler<ListMessagesCommand, ListMessagesResponse>
{

    private readonly UserContext _userContext;
    private readonly ChatsStorage _chatsStorage;
    private readonly MessagesStorage _messagesStorage;

    public ListMessagesCommandHandler(UserContext userContext, ChatsStorage chatsStorage, MessagesStorage messagesStorage)
    {
        _userContext = userContext;
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
    }


    public async Task<ListMessagesResponse> Handle(ListMessagesCommand request, CancellationToken cancellationToken)
    {
        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        try
        {
            long? messageId = request.FromMessageId == 0 ? null : request.FromMessageId;

            if (request.Count is 0 or > 50)
            {
                request.Count = 50;
            }

            var messages = await _messagesStorage.GetChatMessages(request.ChatId, messageId, request.Count);

            return new ListMessagesResponse()
            {
                Messages = { messages.Select(x => x.ToGrpc()) }
            };
        }
        catch (FromMessageNotFoundException)
        {
            throw new MessageNotFoundException();
        }
        
    }
}