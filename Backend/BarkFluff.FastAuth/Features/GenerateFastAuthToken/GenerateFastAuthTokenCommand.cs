using BarkFluff.Proto.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.GenerateFastAuthToken;

public class GenerateFastAuthTokenCommand : IRequest<GenerateFastAuthTokenResponse>
{
    public TokenFormat Format { get; set; }
}
