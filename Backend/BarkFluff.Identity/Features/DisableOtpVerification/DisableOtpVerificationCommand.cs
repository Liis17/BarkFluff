using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.DisableOtpVerification;

public class DisableOtpVerificationCommand : IRequest<DisableOtpVerificationResponse>
{
    public OtpTypeId OptType { get; set; }

    public string? OtpCode { get; set; }
}