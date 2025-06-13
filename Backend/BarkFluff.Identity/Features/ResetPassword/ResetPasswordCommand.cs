using BarkFluff.Identity.Domain;
using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.ResetPassword;

public class ResetPasswordCommand : IRequest<ResetPasswordResponse>
{
    public string? Email { get; set; }
        
    public string? Username { get; set; }
        
    public OtpType OtpType { get; set; }
}