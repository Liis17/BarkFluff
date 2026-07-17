using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.CreateToken;

public class CreateTokenCommand : IRequest<CreateTokenResponse>
{
    public string RefreshToken { get; set; }
    public string? DeviceName { get; set; }
    public string? AppName { get; set; }
    public string? AppVersion { get; set; }
}