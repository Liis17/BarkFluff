using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.DisableOtpVerificationServer;

public class DisableOtpVerificationServerCommand : IRequest<DisableOtpVerificationResponse>
{
    public long UserId { get; set; }

    public OtpTypeId OtpType { get; set; }
}
