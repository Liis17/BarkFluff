using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ListPrivateMessages;

public class ListPrivateMessagesQuery : IRequest<ListPrivateMessagesResponse>
{
    public Guid ChatId { get; set; }

    public long? FromMessageId { get; set; }

    public int OffsetBefore { get; set; }

    public int OffsetAfter { get; set; }
}
