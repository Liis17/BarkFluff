using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.RotateSignedPrekey;

public class RotateSignedPrekeyCommand : IRequest<Unit>
{
    public RotateSignedPrekeyRequest Request { get; set; } = new();
}
