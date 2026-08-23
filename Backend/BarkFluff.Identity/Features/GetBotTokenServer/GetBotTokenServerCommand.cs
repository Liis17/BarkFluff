using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.GetBotTokenServer;

public class GetBotTokenServerCommand : IRequest<GetBotTokenServerResponse>
{
    public long BotUserId { get; set; }

    public string? TokenId { get; set; }
}
