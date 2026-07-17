using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Devices.UpdateDeviceAppInfo;

public class UpdateDeviceAppInfoCommand : IRequest<UpdateDeviceAppInfoResponse>
{
    public Guid DeviceId { get; set; }
    public long UserId { get; set; }
    public string OriginalName { get; set; }
    public string? AppName { get; set; }
}
