using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatMutes.GetMutedChatIds;

public class GetMutedChatIdsQuery : IRequest<GetMutedChatIdsResponse>
{
    public long UserId { get; set; }

    public List<string> ChatIds { get; set; } = [];
}
