using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Helpers;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.SetPassword;

public class SetPasswordCommandHandler : IRequestHandler<SetPasswordCommand>
{
    private readonly UsersStorage _usersStorage;
    private readonly UserContext _userContext;

    public SetPasswordCommandHandler(UsersStorage usersStorage, UserContext userContext)
    {
        _usersStorage = usersStorage;
        _userContext = userContext;
    }
    
    public async Task Handle(SetPasswordCommand request, CancellationToken cancellationToken)
    {
        var passwordHash = PasswordHasher.HashPassword(request.Password);
        
        await _usersStorage.UpdatePasswordHash(_userContext.UserId, passwordHash);
    }
}