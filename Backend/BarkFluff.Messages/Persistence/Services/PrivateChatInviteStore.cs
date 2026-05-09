using StackExchange.Redis;

namespace BarkFluff.Messages.Persistence.Services;

/// <summary>
/// Хранит pending-инвайты приватных чатов в Redis.
/// Используется потому что у Private-чата на момент создания только один участник
/// (инициатор) и нет места в БД, чтобы записать «кому отправлен invite, который ещё не принят».
/// Когда invitee делает Accept — запись удаляется и invitee добавляется в ChatMembers.
/// </summary>
public class PrivateChatInviteStore
{
    private const string Prefix = "private_invite";

    private readonly IConnectionMultiplexer _redis;

    public PrivateChatInviteStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private static string Key(Guid chatId) => $"{Prefix}:{chatId}";

    public Task SetAsync(Guid chatId, long inviteeUserId)
    {
        return _redis.GetDatabase().StringSetAsync(Key(chatId), inviteeUserId);
    }

    public async Task<long?> GetInviteeAsync(Guid chatId)
    {
        var value = await _redis.GetDatabase().StringGetAsync(Key(chatId));
        if (value.IsNullOrEmpty) return null;
        return (long)value;
    }

    public Task<bool> RemoveAsync(Guid chatId)
    {
        return _redis.GetDatabase().KeyDeleteAsync(Key(chatId));
    }
}
