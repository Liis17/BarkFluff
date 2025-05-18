using BarkFluff.Proto.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.ListChats;

public class ListChatsCommand : IRequest<ListChatsResponse>
{
    public int Skip { get; set; }
    
    public int Size { get; set; }
}