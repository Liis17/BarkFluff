using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Devices.RegisterDevice;

public class RegisterDeviceCommand : IRequest<RegisterDeviceResponse>
{
    public Guid DeviceId { get; set; }
    public long UserId { get; set; }
    public string OriginalName { get; set; }
    public string? AppName { get; set; }
    public string? OperationSystem { get; set; }
    public string? Location { get; set; }
}
