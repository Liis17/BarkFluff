using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.RegisterPrekeyBundle;

public class RegisterPrekeyBundleCommand : IRequest<Unit>
{
    public RegisterPrekeyBundleRequest Request { get; set; } = new();
}
