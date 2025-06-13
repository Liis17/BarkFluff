namespace BarkFluff.Identity.Features.SetPassword;

using GrpcServer.XAuth;
using MediatR;
using Persistence.Services;
using Services;

public class SetPasswordCommandHandler : IRequestHandler<SetPasswordCommand>
{
    private readonly UserContext _userContext;
    private readonly PasswordsStorage _passwordsStorage;

    public SetPasswordCommandHandler(UserContext userContext, PasswordsStorage passwordsStorage)
    {
        _userContext = userContext;
        _passwordsStorage = passwordsStorage;
    }

    public async Task Handle(SetPasswordCommand request, CancellationToken cancellationToken)
    {
        var passwordHash = PasswordHasher.HashPassword(request.NewPassword);

        await _passwordsStorage.UpdateUserPasswordHash(_userContext.UserId, passwordHash);
    }
}