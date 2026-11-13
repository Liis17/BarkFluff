using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Domain;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

using UserAccountStatus = BarkFluff.Users.Domain.AccountStatus;
using UserEntity = BarkFluff.Users.Domain.User;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed class SetAccountRestrictionCommandHandler : IRequestHandler<SetAccountRestrictionCommand, SetAccountRestrictionResponse>
{
    private static readonly TimeSpan DeletionDelay = TimeSpan.FromDays(7);

    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;

    public SetAccountRestrictionCommandHandler(UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender)
    {
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
    }

    public async Task<SetAccountRestrictionResponse> Handle(SetAccountRestrictionCommand request, CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (request.Action is AccountRestrictionAction.Block or AccountRestrictionAction.ScheduleDeletion
            && (string.IsNullOrWhiteSpace(reason) || reason.Length > 500))
            throw new InvalidAccountRestrictionTransitionException();

        UserEntity user;
        while (true)
        {
            user = await _usersStorage.GetAccountRestrictionUser(request.UserId, cancellationToken);
            if (user is null)
                throw new UserNotFoundException();
            if (user.IsBot || user.IsDraft || user.AccountStatus == UserAccountStatus.Deleted)
                throw new InvalidAccountRestrictionTransitionException();

            var status = user.AccountStatus;
            var idempotent = false;
            UserAccountStatus targetStatus;
            switch (request.Action)
            {
                case AccountRestrictionAction.Block when status == UserAccountStatus.Blocked:
                    targetStatus = status;
                    idempotent = true;
                    break;
                case AccountRestrictionAction.Block when status == UserAccountStatus.Active:
                    targetStatus = UserAccountStatus.Blocked;
                    break;
                case AccountRestrictionAction.Unblock when status == UserAccountStatus.Active:
                case AccountRestrictionAction.CancelDeletion when status == UserAccountStatus.Active:
                    targetStatus = status;
                    idempotent = true;
                    break;
                case AccountRestrictionAction.Unblock when status == UserAccountStatus.Blocked:
                    targetStatus = UserAccountStatus.Active;
                    break;
                case AccountRestrictionAction.CancelDeletion when status == UserAccountStatus.PendingDeletion:
                    targetStatus = user.PreDeletionAccountStatus == UserAccountStatus.Blocked
                        ? UserAccountStatus.Blocked
                        : UserAccountStatus.Active;
                    break;
                case AccountRestrictionAction.ScheduleDeletion when status == UserAccountStatus.PendingDeletion:
                    targetStatus = status;
                    idempotent = true;
                    break;
                case AccountRestrictionAction.ScheduleDeletion when status is UserAccountStatus.Active or UserAccountStatus.Blocked:
                    targetStatus = UserAccountStatus.PendingDeletion;
                    break;
                default:
                    throw new InvalidAccountRestrictionTransitionException();
            }

            if (idempotent)
            {
                if (await _usersStorage.TouchAccountRestrictionStatus(request.UserId, status, cancellationToken) > 0)
                    break;
                continue;
            }

            var now = DateTime.UtcNow;
            DateTime? deletionScheduledAt = targetStatus == UserAccountStatus.PendingDeletion ? now.Add(DeletionDelay) : null;
            var restoringBlock = request.Action == AccountRestrictionAction.CancelDeletion
                && targetStatus == UserAccountStatus.Blocked;
            var targetReason = restoringBlock
                ? user.PreDeletionRestrictionReason ?? user.RestrictionReason ?? string.Empty
                : reason;
            var targetRestrictedAt = restoringBlock
                ? user.PreDeletionRestrictedAt ?? user.RestrictedAt ?? now
                : now;
            if (await _usersStorage.TrySetAccountRestriction(
                    request.UserId,
                    status,
                    targetStatus,
                    targetReason,
                    targetRestrictedAt,
                    deletionScheduledAt,
                    targetStatus == UserAccountStatus.PendingDeletion ? status : null,
                    targetStatus == UserAccountStatus.PendingDeletion && status == UserAccountStatus.Blocked ? user.RestrictionReason : null,
                    targetStatus == UserAccountStatus.PendingDeletion && status == UserAccountStatus.Blocked ? user.RestrictedAt : null,
                    cancellationToken) > 0)
                break;
        }

        user = await _usersStorage.GetAccountRestrictionUser(request.UserId, cancellationToken)
            ?? throw new UserNotFoundException();

        var displayName = user.AccountStatus switch
        {
            UserAccountStatus.Blocked => ("Заблокированный пользователь", string.Empty),
            UserAccountStatus.PendingDeletion or UserAccountStatus.Deleted => ("Удалённый пользователь", string.Empty),
            _ => (user.FirstName, user.LastName),
        };
        await _userInfoQueueSender.NameChangedEvent(user.Id, displayName.Item1, displayName.Item2);
        await _userInfoQueueSender.UserChangedAvatarEvent(
            user.Id,
            user.AccountStatus == UserAccountStatus.Active ? user.ProfilePicture ?? string.Empty : string.Empty,
            user.AccountStatus == UserAccountStatus.Active ? user.ProfilePicturePreviewUrl ?? string.Empty : string.Empty);

        return new SetAccountRestrictionResponse
        {
            User = user.ToGrpc(includeRestrictedData: true),
            Restriction = user.ToRestrictionInfo(),
            Email = user.Contact?.Email ?? string.Empty,
        };
    }

}
