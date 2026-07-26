using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.CreateBotTokenServer;

public class CreateBotTokenServerCommand : IRequest<CreateBotTokenServerResponse>
{
    public long BotUserId { get; set; }
}
