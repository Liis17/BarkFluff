using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;

namespace BarkFluff.Messages.Features.Shared;

internal static class UserAccountStatusGuard
{
    public static void EnsureActive(AccountStatus status)
    {
        if (status != AccountStatus.Active)
        {
            throw new UserUnavailableException();
        }
    }
}
