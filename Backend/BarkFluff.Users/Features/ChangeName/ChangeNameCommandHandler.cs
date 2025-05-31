using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.ChangeName;

public class ChangeNameCommandHandler : IRequestHandler<ChangeNameCommand>
{
    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;

    public ChangeNameCommandHandler(UserContext userContext, UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
    }

    public async Task Handle(ChangeNameCommand request, CancellationToken cancellationToken)
    {
        await _usersStorage.ChangeName(_userContext.UserId, request.FirstName, request.LastName);

        await _userInfoQueueSender.NameChangedEvent(_userContext.UserId, request.FirstName, request.LastName);
    }
}