using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Devices.DeleteUserDevice;

public class DeleteUserDeviceCommand : IRequest<DeleteUserDeviceResponse>
{
    public Guid DeviceId { get; set; }
    public long UserId { get; set; }
}
