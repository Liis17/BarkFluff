using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.ListPeerDevices;

public class ListPeerDevicesQueryHandler(PrekeyStorage prekeyStorage)
    : IRequestHandler<ListPeerDevicesQuery, ListPeerDevicesResponse>
{
    public async Task<ListPeerDevicesResponse> Handle(ListPeerDevicesQuery query, CancellationToken cancellationToken)
    {
        var devices = await prekeyStorage.ListPeerDevicesAsync(query.UserId);

        var response = new ListPeerDevicesResponse();
        foreach (var (device, hasBundle) in devices)
        {
            response.Devices.Add(device.ToPeerInfoGrpc(hasBundle));
        }

        return response;
    }
}
