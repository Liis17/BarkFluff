using BarkFluff.Proto.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.RejectFastAuth;

public class RejectFastAuthCommand : IRequest<RejectFastAuthResponse>
{
    public string FastAuthId { get; set; } = string.Empty;

    public string ConfirmationCode { get; set; } = string.Empty;
}
