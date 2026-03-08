using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.EnableOtpVerification;

public class EnableOtpVerificationCommand : IRequest<EnableOtpVerificationResponse>
{
    public OtpTypeId OptType { get; set; }
}