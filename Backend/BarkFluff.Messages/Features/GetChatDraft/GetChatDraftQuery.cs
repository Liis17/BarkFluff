using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetChatDraft;

public class GetChatDraftQuery : IRequest<GetChatDraftResponse>
{
    public Guid ChatId { get; set; }
}
