using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ListPinnedMessages;

public class ListPinnedMessagesQuery : IRequest<ListPinnedMessagesResponse>
{
    public Guid ChatId { get; set; }

    public int Skip { get; set; }

    public int Count { get; set; }
}
