using BarkFluff.Proto.Users;

namespace BarkFluff.Users.Mapping;

public static class PrivacyMapping
{
    public static PrivacySettings ToGrpc(this Domain.Privacy domain)
    {
        return new PrivacySettings
        {
            ProfileVisibleOnSite = domain.ProfileVisibleOnSite,
            AvatarVisibility = (ProfileFieldVisibility)(int)domain.AvatarVisibility,
            BioVisibility = (ProfileFieldVisibility)(int)domain.BioVisibility,
            EmailVisibility = (ProfileFieldVisibility)(int)domain.EmailVisibility,
            SearchVisible = domain.SearchVisible,
            OnlineVisibility = (ProfileFieldVisibility)(int)domain.OnlineVisibility,
        };
    }

    public static Domain.Privacy ToDomain(this PrivacySettings grpc)
    {
        return new Domain.Privacy
        {
            ProfileVisibleOnSite = grpc.ProfileVisibleOnSite,
            AvatarVisibility = (Domain.ProfileFieldVisibility)(int)grpc.AvatarVisibility,
            BioVisibility = (Domain.ProfileFieldVisibility)(int)grpc.BioVisibility,
            EmailVisibility = (Domain.ProfileFieldVisibility)(int)grpc.EmailVisibility,
            SearchVisible = grpc.SearchVisible,
            OnlineVisibility = (Domain.ProfileFieldVisibility)(int)grpc.OnlineVisibility,
        };
    }
}
