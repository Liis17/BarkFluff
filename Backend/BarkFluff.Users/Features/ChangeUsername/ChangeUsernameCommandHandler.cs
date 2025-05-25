using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Features.ChangeName;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.ChangeUsername;

public class ChangeUsernameCommandHandler : IRequestHandler<ChangeUsernameCommand>
{
    
    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;


    public ChangeUsernameCommandHandler(UserContext userContext, UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
    }

    public async Task Handle(ChangeUsernameCommand request, CancellationToken cancellationToken)
    {
        await _usersStorage.ChangeUsername(_userContext.UserId, request.Username);
        
        await _userInfoQueueSender.UsernameChangedEvent(_userContext.UserId, request.Username);
    }
}