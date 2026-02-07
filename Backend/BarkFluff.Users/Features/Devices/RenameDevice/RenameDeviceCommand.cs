using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Devices.RenameDevice;

public class RenameDeviceCommand : IRequest<RenameDeviceResponse>
{
    public Guid DeviceId { get; set; }
    public string CustomName { get; set; }
}
