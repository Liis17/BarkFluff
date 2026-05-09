using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.UnpinAll;

public class UnpinAllCommand : IRequest<UnpinAllResponse>
{
    public Guid ChatId { get; set; }
}
