using BarkFluff.Proto.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.CreateGroupChat;

public class CreateGroupChatCommand : IRequest<CreateGroupChatResponse>
{
    public List<long> UserIds { get; set; }
    
    public string Title { get; set; }

    public Guid? PictureFileId { get; set; }
    
}