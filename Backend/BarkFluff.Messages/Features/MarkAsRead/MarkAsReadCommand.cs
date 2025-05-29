using MediatR;

namespace BarkFluff.Messages.Features.MarkAsRead;

public class MarkAsReadCommand : IRequest
{
    public List<long> MessageIds { get; set; }
} 