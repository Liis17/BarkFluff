namespace BarkFluff.Identity.Features.SetPassword;

using GrpcServer.XAuth;
using MediatR;
using Persistence.Services;
using Services;

public class SetPasswordCommandHandler : IRequestHandler<SetPasswordCommand>
{
    private readonly UserContext _userContext;
    private readonly PasswordsStorage _passwordsStorage;
    private readonly RefreshTokensStorage refreshTokensStorage;
    
    public SetPasswordCommandHandler(UserContext userContext, PasswordsStorage passwordsStorage, RefreshTokensStorage refreshTokensStorage)
    {
        _userContext = userContext;
        _passwordsStorage = passwordsStorage;
        this.refreshTokensStorage = refreshTokensStorage;
    }

    public async Task Handle(SetPasswordCommand request, CancellationToken cancellationToken)
    {
        var passwordHash = PasswordHasher.HashPassword(request.NewPassword);

        await _passwordsStorage.UpdateUserPasswordHash(_userContext.UserId, passwordHash);
    }
}