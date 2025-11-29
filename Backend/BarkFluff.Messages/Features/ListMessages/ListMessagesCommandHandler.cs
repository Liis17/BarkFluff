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

            // Check if new bi-directional pagination is requested
            if (request.OffsetBefore > 0 || request.OffsetAfter > 0)
            {
                // Clamp offsets to maximum of 50
                var offsetBefore = Math.Min(request.OffsetBefore, 50);
                var offsetAfter = Math.Min(request.OffsetAfter, 50);

                var messagesWithOffset = await _messagesStorage.GetChatMessagesWithOffset(
                    request.ChatId, messageId, offsetBefore, offsetAfter);

                return new ListMessagesResponse()
                {
                    Messages = { messagesWithOffset.Select(x => x.ToGrpc()) }
                };
            }

            // Legacy behavior for backward compatibility
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