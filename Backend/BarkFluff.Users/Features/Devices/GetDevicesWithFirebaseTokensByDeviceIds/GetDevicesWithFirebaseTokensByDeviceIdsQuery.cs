using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokensByDeviceIds;

public class GetDevicesWithFirebaseTokensByDeviceIdsQuery : IRequest<GetDevicesWithFirebaseTokensResponse>
{
    public List<Guid> DeviceIds { get; set; } = [];
}
