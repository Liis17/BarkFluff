using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.ForceSetPasswordServer;

public class ForceSetPasswordServerCommand : IRequest<ForceSetPasswordServerResponse>
{
    public long UserId { get; set; }
    public string NewPassword { get; set; } = string.Empty;
}
