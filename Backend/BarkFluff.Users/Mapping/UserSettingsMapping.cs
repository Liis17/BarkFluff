using BarkFluff.Proto.Users;

namespace BarkFluff.Users.Mapping;

public static class UserSettingsMapping
{
    public static UserSettingsData ToGrpc(
        this Domain.UserSettings settings,
        IEnumerable<Domain.UserChatSettings> chatSettings)
    {
        var result = new UserSettingsData
        {
            GlobalChatBackgroundFileId = settings.GlobalChatBackgroundFileId ?? string.Empty,
        };

        result.ChatBackgrounds.AddRange(chatSettings.Select(s => new ChatBackgroundOverride
        {
            ChatId = s.ChatId.ToString(),
            ChatBackgroundFileId = s.ChatBackgroundFileId,
        }));

        return result;
    }
}
