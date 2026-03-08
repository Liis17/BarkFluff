using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.CreateToken;

public class CreateTokenCommand : IRequest<CreateTokenResponse>
{
    public string RefreshToken { get; set; }
}