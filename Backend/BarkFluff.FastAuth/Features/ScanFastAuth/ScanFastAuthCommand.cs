using BarkFluff.Proto.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.ScanFastAuth;

public class ScanFastAuthCommand : IRequest<ScanFastAuthResponse>
{
    public string FastAuthId { get; set; } = string.Empty;
}
