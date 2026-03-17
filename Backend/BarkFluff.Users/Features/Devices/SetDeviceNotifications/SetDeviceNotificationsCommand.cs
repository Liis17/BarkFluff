using MediatR;

namespace BarkFluff.Users.Features.Devices.SetDeviceNotifications;

public class SetDeviceNotificationsCommand : IRequest<Unit>
{
    public bool Enabled { get; set; }
}
