using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckChatMembership;

public class CheckChatMembershipQueryHandler
    : IRequestHandler<CheckChatMembershipQuery, CheckChatMembershipResponse>
{
    private readonly ChatsStorage _chatsStorage;

    public CheckChatMembershipQueryHandler(ChatsStorage chatsStorage)
    {
        _chatsStorage = chatsStorage;
    }

    public async Task<CheckChatMembershipResponse> Handle(
        CheckChatMembershipQuery request,
        CancellationToken cancellationToken)
    {
        // Парсим только валидные Guid — мусор молча отбрасываем (не член ни одного чата).
        var parsed = new List<Guid>();
        foreach (var raw in request.ChatIds.Distinct())
        {
            if (Guid.TryParse(raw, out var chatId))
            {
                parsed.Add(chatId);
            }
        }

        var response = new CheckChatMembershipResponse();

        if (parsed.Count == 0)
        {
            return response;
        }

        var membership = await _chatsStorage.GetMembershipContext(
            request.UserId, request.UserUuid, parsed);

        response.MemberChatIds.AddRange(membership.MemberChatIds.Select(id => id.ToString()));

        if (membership.RequesterUuid.HasValue)
        {
            response.RequesterUuid = membership.RequesterUuid.Value.ToString();
        }

        // Нефедеративные чаты в federated_chats не попадают вовсе: пустой список —
        // рабочий случай подавляющего большинства вызовов.
        foreach (var chat in membership.FederatedPeers.GroupBy(p => p.ChatId))
        {
            var federated = new FederatedChatContext { ChatId = chat.Key.ToString() };

            federated.Peers.AddRange(chat.Select(peer => new FederatedChatPeer
            {
                UserUuid = peer.UserUuid.ToString(),
                ServerName = peer.ServerName,
            }));

            response.FederatedChats.Add(federated);
        }

        return response;
    }
}
