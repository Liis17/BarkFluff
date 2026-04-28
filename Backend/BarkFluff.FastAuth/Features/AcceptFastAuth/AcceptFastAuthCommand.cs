using BarkFluff.Proto.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.AcceptFastAuth;

public class AcceptFastAuthCommand : IRequest<AcceptFastAuthResponse>
{
    public string FastAuthId { get; set; } = string.Empty;

    public string ConfirmationCode { get; set; } = string.Empty;
}
