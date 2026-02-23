using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.RemoveActiveSessionServer;

public class RemoveActiveSessionServerCommand : IRequest<RemoveActiveSessionResponse>
{
    public long UserId { get; set; }

    public string DeviceId { get; set; } = string.Empty;
}
