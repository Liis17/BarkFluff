using MediatR;

namespace BarkFluff.Messages.Features.DeleteChatDraft;

public class DeleteChatDraftCommand : IRequest<bool>
{
    public Guid ChatId { get; set; }

    public Guid Revision { get; set; }
}
