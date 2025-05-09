using BarkFluff.Identity.Features.Shared;
using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.Auth;

public class AuthCommand : DeviceNameCommand, IRequest<AuthResponse>
{
    public string? Username { get; set; }
    
    public string? Email { get; set; }
    
    public string? Password { get; set; }
    
    public string? OtpCode { get; set; }
}