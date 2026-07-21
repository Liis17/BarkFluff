using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetChatMemberIds;

public class GetChatMemberIdsQueryHandler
    : IRequestHandler<GetChatMemberIdsQuery, GetChatMemberIdsResponse>
{
    private readonly ChatsStorage _chatsStorage;

    public GetChatMemberIdsQueryHandler(ChatsStorage chatsStorage)
    {
        _chatsStorage = chatsStorage;
    }

    public async Task<GetChatMemberIdsResponse> Handle(
        GetChatMemberIdsQuery request,
        CancellationToken cancellationToken)
    {
        var response = new GetChatMemberIdsResponse();

        // Невалидный Guid — пустой список участников (звонить некому).
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            return response;
        }

        var members = await _chatsStorage.GetChatMembers(chatId, 0, int.MaxValue);

        response.UserIds.AddRange(members.LocalUserIds());

        return response;
    }
}
