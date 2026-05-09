using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.ListPeerDevices;

public class ListPeerDevicesQuery : IRequest<ListPeerDevicesResponse>
{
    public long UserId { get; set; }
}
