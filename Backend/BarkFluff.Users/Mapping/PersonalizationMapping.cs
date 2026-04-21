using BarkFluff.Proto.Users;

namespace BarkFluff.Users.Mapping;

public static class PersonalizationMapping
{
    public static UserPersonalizationData ToGrpc(this Domain.UserPersonalization domain)
    {
        var data = new UserPersonalizationData
        {
            ProfilePosterFileId = domain.ProfilePosterFileId ?? string.Empty,
        };
        data.ChatBackgroundFileIds.AddRange(domain.ChatBackgroundFileIds);
        return data;
    }
}
