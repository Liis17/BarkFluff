using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.UpdateBotProfile;

public class UpdateBotProfileCommand : IRequest<UpdateBotProfileResponse>
{
    public long BotId { get; set; }

    public string? Name { get; set; }

    public string? Username { get; set; }
}
