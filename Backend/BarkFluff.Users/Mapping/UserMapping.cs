using BarkFluff.Proto.Users;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Mapping;

public static class UserMapping
{
    public static User ToGrpc(this Domain.User domainUser)
    {
        return new User
        {
            FirstName = domainUser.FirstName,
            Id = domainUser.Id,
            LastName = domainUser.LastName,
            RegistrationDate = Timestamp.FromDateTime(domainUser.RegistrationDate),
            Username = domainUser.Username,
            ProfilePicture = domainUser.ProfilePicture ?? string.Empty,
            ProfilePicturePreview = domainUser.ProfilePicturePreviewUrl ?? string.Empty,
            Bio = domainUser.Bio ?? string.Empty,
            StorageLimitGb = domainUser.StorageLimitGb,
            IsBot = domainUser.IsBot,
        };
    }
}