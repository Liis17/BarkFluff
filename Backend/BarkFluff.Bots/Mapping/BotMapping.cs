using BarkFluff.Bots.Domain;
using BarkFluff.Proto.Bots;

namespace BarkFluff.Bots.Mapping;

public static class BotMapping
{
    public static GetMeResponse ToGetMeResponse(this Bot bot) => new()
    {
        Id = bot.Id,
        IsBot = true,
        FirstName = bot.Name,
        Username = bot.Username,
    };

    /// <summary>getMe для HTTP Bot API (Telegram-like JSON).</summary>
    public static object ToHttpResult(this GetMeResponse response) => new
    {
        id = response.Id,
        is_bot = true,
        first_name = response.FirstName,
        username = response.Username,
    };

    /// <summary>getFile для HTTP Bot API.</summary>
    public static object ToHttpResult(this GetFileResponse response) => new
    {
        file_id = response.FileId,
        file_name = response.FileName,
        file_size = response.FileSize,
        file_url = response.FileUrl,
    };

    /// <summary>getUserInfo для HTTP Bot API.</summary>
    public static object ToHttpResult(this GetUserInfoResponse response) => new
    {
        id = response.Id,
        username = response.Username,
        first_name = response.FirstName,
        last_name = response.LastName,
        bio = response.Bio,
        avatar_url = response.AvatarUrl,
        is_bot = response.IsBot,
    };
}
