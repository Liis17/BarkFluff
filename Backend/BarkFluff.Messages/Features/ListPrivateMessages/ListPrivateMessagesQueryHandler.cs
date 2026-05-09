using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ListPrivateMessages;

public class ListPrivateMessagesQueryHandler : IRequestHandler<ListPrivateMessagesQuery, ListPrivateMessagesResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly EncryptedMessagesStorage _storage;
    private readonly UserContext _userContext;

    public ListPrivateMessagesQueryHandler(
        ChatsStorage chatsStorage,
        EncryptedMessagesStorage storage,
        UserContext userContext)
    {
        _chatsStorage = chatsStorage;
        _storage = storage;
        _userContext = userContext;
    }

    public async Task<ListPrivateMessagesResponse> Handle(ListPrivateMessagesQuery request, CancellationToken cancellationToken)
    {
        var chat = await _chatsStorage.GetChat(request.ChatId);
        if (chat is null)
        {
            throw new ChatNotFoundException();
        }

        if (chat.Type != ChatType.Private)
        {
            throw new ChatNotPrivateException();
        }

        if (chat.Members?.All(m => m.UserId != _userContext.UserId) ?? true)
        {
            throw new NoAccessToChatException();
        }

        var messages = await _storage.ListByChatAsync(
            request.ChatId,
            request.FromMessageId is 0 ? null : request.FromMessageId,
            request.OffsetBefore,
            request.OffsetAfter);

        var response = new ListPrivateMessagesResponse();
        foreach (var msg in messages)
        {
            response.Messages.Add(msg.ToGrpc());
        }

        return response;
    }
}
