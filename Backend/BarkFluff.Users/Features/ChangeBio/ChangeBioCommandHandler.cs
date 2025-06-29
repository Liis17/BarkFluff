using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using BarkFluff.Shared.Exceptions.Users;

namespace BarkFluff.Users.Features.ChangeBio;

public class ChangeBioCommandHandler : IRequestHandler<ChangeBioCommand>
{
        
    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;


    public ChangeBioCommandHandler(UserContext userContext, UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
    }

    public async Task Handle(ChangeBioCommand request, CancellationToken cancellationToken)
    {
        if (request.Bio != null && request.Bio.Length > 200)
            throw new BioTooLongException();

        await _usersStorage.ChangeBio(_userContext.UserId, request.Bio);
        
        await _userInfoQueueSender.UserBioChangedEvent(_userContext.UserId, request.Bio);
    }
}