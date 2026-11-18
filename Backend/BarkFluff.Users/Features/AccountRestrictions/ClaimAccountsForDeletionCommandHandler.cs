using BarkFluff.Proto.Users;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed class ClaimAccountsForDeletionCommandHandler : IRequestHandler<ClaimAccountsForDeletionCommand, ClaimAccountsForDeletionResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;

    public ClaimAccountsForDeletionCommandHandler(UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender)
    {
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
    }

    public async Task<ClaimAccountsForDeletionResponse> Handle(ClaimAccountsForDeletionCommand request, CancellationToken cancellationToken)
    {
        var userIds = await _usersStorage.ClaimAccountsForDeletion(request.Limit, cancellationToken);
        foreach (var userId in userIds)
        {
            await _userInfoQueueSender.NameChangedEvent(userId, "Удалённый пользователь", string.Empty);
            await _userInfoQueueSender.UserChangedAvatarEvent(userId, string.Empty, string.Empty);
        }

        var response = new ClaimAccountsForDeletionResponse();
        response.UserIds.AddRange(userIds);
        return response;
    }
}
