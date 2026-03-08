using BarkFluff.Messages.Domain;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ListChatAttachments;

public class ListChatAttachmentsCommand : IRequest<ListChatAttachmentsResponse>
{
    public Guid ChatId { get; set; }

    public int Skip { get; set; }

    public int Size { get; set; }

    public MessageAttachmentType? AttachmentType { get; set; }

    public bool SortDescending { get; set; } = true;
}
