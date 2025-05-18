using BarkFluff.Proto.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.ListChatMembers;

public class ListChatMembersCommand : IRequest<ListChatMembersResponse>
{
    public int Count { get; set; }
    
    public int Skip { get; set; }
    
    public Guid ChatId { get; set; }
}