using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Helpers;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.SetPassword;

public class SetPasswordCommandHandler : IRequestHandler<SetPasswordCommand>
{
    private readonly UsersStorage _usersStorage;
    private readonly UserContext _userContext;
    private readonly UserInfoQueueSender _userInfoQueueSender;

    public SetPasswordCommandHandler(UsersStorage usersStorage, UserContext userContext, UserInfoQueueSender userInfoQueueSender)
    {
        _usersStorage = usersStorage;
        _userContext = userContext;
        _userInfoQueueSender = userInfoQueueSender;
    }
    
    public async Task Handle(SetPasswordCommand request, CancellationToken cancellationToken)
    {
        var passwordHash = PasswordHasher.HashPassword(request.Password);
        
        await _usersStorage.UpdatePasswordHash(_userContext.UserId, passwordHash);
        
        await _userInfoQueueSender.UserChangedPasswordEvent(_userContext.UserId);
    }
}