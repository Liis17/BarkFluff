using BarkFluff.Bots.Host.Http;
using BarkFluff.Bots.Services;

namespace BarkFluff.Bots.Mapping;

public static class BotUpdateMapping
{
    /// <summary>Update для HTTP getUpdates (Telegram-like JSON).</summary>
    public static UpdateResult ToUpdateResult(this Domain.BotUpdate update)
    {
        var payload = UpdateJsonMapper.ParsePayload(update.Payload);
        return new UpdateResult { UpdateId = update.Id, Message = payload.Message };
    }
}
