using MediatR;

namespace BarkFluff.Users.Features.Devices.SetNotificationsEnabled;

public class SetNotificationsEnabledCommand : IRequest<Unit>
{
    public bool Enabled { get; set; }
}
