using BarkFluff.Proto.Users;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Mapping;

public static class UserMapping
{
    public static AccountRestrictionInfo ToRestrictionInfo(this Domain.User domainUser)
    {
        return new AccountRestrictionInfo
        {
            Status = (AccountStatus)(int)domainUser.AccountStatus,
            Reason = domainUser.RestrictionReason ?? string.Empty,
            RestrictedAt = domainUser.RestrictedAt.HasValue
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(domainUser.RestrictedAt.Value, DateTimeKind.Utc))
                : null,
            DeletionScheduledAt = domainUser.DeletionScheduledAt.HasValue
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(domainUser.DeletionScheduledAt.Value, DateTimeKind.Utc))
                : null,
        };
    }

    public static User ToGrpc(this Domain.User domainUser, bool includeRestrictedData = false)
    {
        var isActive = domainUser.AccountStatus == Domain.AccountStatus.Active;
        var includeProfile = isActive || includeRestrictedData;
        return new User
        {
            FirstName = includeProfile ? domainUser.FirstName : domainUser.AccountStatus == Domain.AccountStatus.Blocked
                ? "Заблокированный пользователь"
                : "Удалённый пользователь",
            Id = domainUser.Id,
            LastName = includeProfile ? domainUser.LastName : string.Empty,
            RegistrationDate = Timestamp.FromDateTime(DateTime.SpecifyKind(domainUser.RegistrationDate, DateTimeKind.Utc)),
            Username = includeProfile ? domainUser.Username : string.Empty,
            ProfilePicture = includeProfile ? domainUser.ProfilePicture ?? string.Empty : string.Empty,
            ProfilePicturePreview = includeProfile ? domainUser.ProfilePicturePreviewUrl ?? string.Empty : string.Empty,
            Bio = includeProfile ? domainUser.Bio ?? string.Empty : string.Empty,
            StorageLimitGb = domainUser.StorageLimitGb,
            IsBot = domainUser.IsBot,
            Uuid = domainUser.Uuid.ToString(),
            AccountStatus = (AccountStatus)(int)domainUser.AccountStatus,
        };
    }
}
