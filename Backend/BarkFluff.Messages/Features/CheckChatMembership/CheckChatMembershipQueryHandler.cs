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

        var memberChatIds = await _chatsStorage.GetMemberChatIds(request.UserId, parsed);

        response.MemberChatIds.AddRange(memberChatIds.Select(id => id.ToString()));

        return response;
    }
}
