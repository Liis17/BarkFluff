using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;

namespace BarkFluff.Identity.Services;

internal static class AccountRestrictionStatusGuard
{
    public static void ThrowIfInactive(AccountStatus status)
    {
        if (status == AccountStatus.Blocked)
            throw new AccountBlockedException();
        if (status != AccountStatus.Active)
            throw new AccountPendingDeletionException();
    }
}
