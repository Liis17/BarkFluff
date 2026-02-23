using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.ListOtpVerificationServer;

public class ListOtpVerificationServerCommand : IRequest<ListOtpVerificationResponse>
{
    public long UserId { get; set; }
}
