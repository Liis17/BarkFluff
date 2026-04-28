using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.CreateSessionForUserServer;

public class CreateSessionForUserServerCommand : IRequest<CreateSessionForUserServerResponse>
{
    public long UserId { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string OperationSystem { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;
}
