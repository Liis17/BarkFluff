using BarkFluff.Shared.Exceptions.Identity;

namespace BarkFluff.Users.Domain;

public static class AccountRestrictionGuard
{
    public static void EnsureActive(User? user)
    {
        if (user is null)
            throw new UserNotFoundException();
        if (user.AccountStatus != AccountStatus.Active)
            throw new InvalidAccountRestrictionTransitionException();
    }
}
