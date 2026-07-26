using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetMe;

public class GetMeQuery : IRequest<GetMeResponse>
{
    public long BotId { get; set; }
}
