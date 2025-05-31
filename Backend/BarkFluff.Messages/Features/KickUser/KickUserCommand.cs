using MediatR;

namespace BarkFluff.Messages.Features.KickUser;

public class KickUserCommand : IRequest
{
    public Guid ChatId { get; set; }
    
    public long UserId { get; set; }
}