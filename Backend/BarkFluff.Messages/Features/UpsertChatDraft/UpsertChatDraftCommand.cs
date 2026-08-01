using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.UpsertChatDraft;

public class UpsertChatDraftCommand : IRequest<UpsertChatDraftResponse>
{
    public Guid ChatId { get; set; }

    public string Text { get; set; } = string.Empty;

    public long? ReplyToMessageId { get; set; }
}
