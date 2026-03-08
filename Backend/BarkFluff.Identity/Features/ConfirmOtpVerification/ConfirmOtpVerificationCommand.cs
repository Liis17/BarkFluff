using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.ConfirmOtpVerification;

public class ConfirmOtpVerificationCommand : IRequest<ConfirmOtpVerificationResponse>
{
    public string OtpCode { get; set; }
}