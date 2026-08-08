using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetChatInfoServer;

public class GetChatInfoServerQueryHandler
    : IRequestHandler<GetChatInfoServerQuery, GetChatInfoServerResponse>
{
    private readonly ChatsStorage _chatsStorage;

    public GetChatInfoServerQueryHandler(ChatsStorage chatsStorage)
    {
        _chatsStorage = chatsStorage;
    }

    public async Task<GetChatInfoServerResponse> Handle(
        GetChatInfoServerQuery request,
        CancellationToken cancellationToken)
    {
        var response = new GetChatInfoServerResponse();

        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            return response;
        }

        // userId не участвует в вычислении title/picture/is_group_chat — только в unread-полях,
        // которые здесь не используются, поэтому безопасно передать 0.
        var chatInfo = await _chatsStorage.GetChatInfo(chatId, 0);

        if (chatInfo is null)
        {
            return response;
        }

        response.Found = true;
        response.Title = chatInfo.Title ?? string.Empty;
        response.Picture = chatInfo.Picture ?? string.Empty;
        response.IsGroupChat = chatInfo.IsGroupChat;

        var members = await _chatsStorage.GetChatMembers(chatId, 0, int.MaxValue);
        response.MemberIds.AddRange(members.LocalUserIds());

        return response;
    }
}
