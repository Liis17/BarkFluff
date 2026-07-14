using MediatR;

namespace BarkFluff.Bots.Features.GetBotUpdates;

/// <summary>Long-poll получение update'ов бота (HTTP getUpdates).</summary>
public class GetBotUpdatesQuery : IRequest<List<Domain.BotUpdate>>
{
    public long BotId { get; set; }

    public long Offset { get; set; }

    public int Limit { get; set; }

    public int TimeoutSeconds { get; set; }
}
