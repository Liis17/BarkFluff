using BarkFluff.Identity.Features.Shared;
using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.EnableOtpVerification;

public class EnableOtpVerificationCommand : DeviceNameCommand, IRequest<EnableOtpVerificationResponse>
{
    public OtpTypeId OptType { get; set; }
}