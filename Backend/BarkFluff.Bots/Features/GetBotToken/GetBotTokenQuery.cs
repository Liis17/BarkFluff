using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetBotToken;

public class GetBotTokenQuery : IRequest<GetBotTokenResponse>
{
    public long BotId { get; set; }
}
