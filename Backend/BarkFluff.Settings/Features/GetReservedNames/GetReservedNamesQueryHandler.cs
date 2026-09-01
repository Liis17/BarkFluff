using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using MediatR;

namespace BarkFluff.Settings.Features.GetReservedNames;

public sealed class GetReservedNamesQueryHandler(SettingsStorage storage)
    : IRequestHandler<GetReservedNamesQuery, GetReservedNamesResponse>
{
    public async Task<GetReservedNamesResponse> Handle(GetReservedNamesQuery request, CancellationToken cancellationToken)
    {
        var response = new GetReservedNamesResponse();
        response.Names.AddRange(await storage.GetReservedNamesAsync(cancellationToken));
        return response;
    }
}
