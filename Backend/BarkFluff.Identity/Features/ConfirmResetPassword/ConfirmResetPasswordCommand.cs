using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.ConfirmResetPassword
{
    public class ConfirmResetPasswordCommand : IRequest
    {
        public Guid ResetId { get; set; }
        
        public string OtpCode { get; set; }
        
        public string NewPassword { get; set; }
    }
}