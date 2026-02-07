using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Devices.GetUserDevices;

public class GetUserDevicesQuery : IRequest<GetUserDevicesResponse>
{
    public long UserId { get; set; }
}
