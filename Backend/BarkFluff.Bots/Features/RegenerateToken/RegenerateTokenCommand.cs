using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.RegenerateToken;

public class RegenerateTokenCommand : IRequest<RegenerateTokenResponse>
{
    public long BotId { get; set; }
}
