using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Proto.Bots;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Bots.Features.ListBots;

public class ListBotsQueryHandler : IRequestHandler<ListBotsQuery, ListBotsResponse>
{
    private readonly BotsStorage _botsStorage;

    public ListBotsQueryHandler(BotsStorage botsStorage)
    {
        _botsStorage = botsStorage;
    }

    public async Task<ListBotsResponse> Handle(ListBotsQuery request, CancellationToken cancellationToken)
    {
        var bots = await _botsStorage.GetAll();

        var response = new ListBotsResponse();
        response.Bots.AddRange(bots.Select(b => new BotInfo
        {
            Id = b.Id,
            Username = b.Username,
            Name = b.Name,
            OwnerUserId = b.OwnerUserId ?? 0,
            SystemRole = (int)b.SystemRole,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc)),
        }));

        return response;
    }
}
