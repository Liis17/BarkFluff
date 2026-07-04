using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatMutes.GetMutedChatIds;

public class GetMutedChatIdsQueryHandler(ChatMuteStorage chatMuteStorage)
    : IRequestHandler<GetMutedChatIdsQuery, GetMutedChatIdsResponse>
{
    public async Task<GetMutedChatIdsResponse> Handle(GetMutedChatIdsQuery request, CancellationToken cancellationToken)
    {
        var chatIds = new List<Guid>(request.ChatIds.Count);
        foreach (var raw in request.ChatIds)
        {
            if (Guid.TryParse(raw, out var id))
            {
                chatIds.Add(id);
            }
        }

        var muted = await chatMuteStorage.GetMutedChatIds(request.UserId, chatIds);

        var response = new GetMutedChatIdsResponse();
        response.MutedChatIds.AddRange(muted.Select(id => id.ToString()));
        return response;
    }
}
