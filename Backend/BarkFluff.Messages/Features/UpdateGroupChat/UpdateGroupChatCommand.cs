using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.UpdateGroupChat;

public class UpdateGroupChatCommand : IRequest<UpdateGroupChatResponse>
{
    public Guid ChatId { get; set; }

    public string? Title { get; set; }

    public Guid? PictureFileId { get; set; }
}
