using System.Text.Json;

using BarkFluff.Messages.Persistence.Services.Dtos;

using StackExchange.Redis;

namespace BarkFluff.Messages.Persistence.Services;

/// <summary>
/// Буфер секретных сообщений и инвайтов на Redis.
/// Сервер хранит opaque envelope не более 24 часов; после Ack клиента ключ удаляется.
/// </summary>
public class SecretMessageBuffer
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private const string MessageKeyPrefix = "secret_msg";       // secret_msg:{deviceId}:{messageId}
    private const string MessageIndexPrefix = "secret_msgs";    // secret_msgs:{deviceId}  (Redis SET)
    private const string InviteKeyPrefix = "secret_invite";     // secret_invite:{deviceId}:{inviteId}
    private const string InviteIndexPrefix = "secret_invites";  // secret_invites:{deviceId} (Redis SET)

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SecretMessageBuffer> _logger;

    public SecretMessageBuffer(IConnectionMultiplexer redis, ILogger<SecretMessageBuffer> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();

    private static string MessageKey(Guid deviceId, string messageId) => $"{MessageKeyPrefix}:{deviceId}:{messageId}";
    private static string MessageIndex(Guid deviceId) => $"{MessageIndexPrefix}:{deviceId}";
    private static string InviteKey(Guid deviceId, string inviteId) => $"{InviteKeyPrefix}:{deviceId}:{inviteId}";
    private static string InviteIndex(Guid deviceId) => $"{InviteIndexPrefix}:{deviceId}";

    public TimeSpan Ttl => DefaultTtl;

    /// <summary>
    /// Кладёт envelope в буфер. Возвращает (messageId, expiresAt).
    /// </summary>
    public virtual async Task<(string MessageId, DateTime ExpiresAt)> EnqueueMessageAsync(
        long senderUserId,
        Guid senderDeviceId,
        Guid recipientDeviceId,
        byte[] envelope)
    {
        var messageId = Guid.NewGuid().ToString();
        var sentAt = DateTime.UtcNow;
        var record = new SecretMessageRecord
        {
            MessageId = messageId,
            SenderUserId = senderUserId,
            SenderDeviceId = senderDeviceId,
            RecipientDeviceId = recipientDeviceId,
            Envelope = envelope,
            SentAt = sentAt,
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(record);

        var batch = Db.CreateBatch();
        var setTask = batch.StringSetAsync(MessageKey(recipientDeviceId, messageId), payload, DefaultTtl);
        var indexTask = batch.SetAddAsync(MessageIndex(recipientDeviceId), messageId);
        var indexExpireTask = batch.KeyExpireAsync(MessageIndex(recipientDeviceId), DefaultTtl + TimeSpan.FromMinutes(5));
        batch.Execute();
        await Task.WhenAll(setTask, indexTask, indexExpireTask);

        var expiresAt = sentAt.Add(DefaultTtl);
        _logger.LogDebug(
            "Запись секретного сообщения {MessageId} для устройства {RecipientDeviceId} (sender={SenderUserId}) — TTL {Ttl}",
            messageId, recipientDeviceId, senderUserId, DefaultTtl);

        return (messageId, expiresAt);
    }

    /// <summary>
    /// Подтверждает доставку секретного сообщения — удаляет envelope и запись из индекса.
    /// </summary>
    public virtual async Task<bool> AckMessageAsync(Guid recipientDeviceId, string messageId)
    {
        var batch = Db.CreateBatch();
        var deleteTask = batch.KeyDeleteAsync(MessageKey(recipientDeviceId, messageId));
        var unindexTask = batch.SetRemoveAsync(MessageIndex(recipientDeviceId), messageId);
        batch.Execute();
        var deleted = await deleteTask;
        await unindexTask;
        return deleted;
    }

    /// <summary>
    /// Возвращает все pending envelope для устройства (например, после переподключения к стриму).
    /// Параллельно очищает индекс от записей, которые уже истекли по TTL.
    /// </summary>
    public virtual async Task<List<SecretMessageRecord>> ListPendingMessagesAsync(Guid recipientDeviceId)
    {
        var ids = await Db.SetMembersAsync(MessageIndex(recipientDeviceId));
        if (ids.Length == 0)
        {
            return new List<SecretMessageRecord>();
        }

        var keys = ids.Select(id => (RedisKey)MessageKey(recipientDeviceId, id!)).ToArray();
        var values = await Db.StringGetAsync(keys);

        var records = new List<SecretMessageRecord>(values.Length);
        var staleIds = new List<RedisValue>();

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].IsNullOrEmpty)
            {
                staleIds.Add(ids[i]);
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<SecretMessageRecord>((byte[])values[i]!);
                if (record != null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Не удалось десериализовать запись секретного сообщения {MessageId}", ids[i]);
                staleIds.Add(ids[i]);
            }
        }

        if (staleIds.Count > 0)
        {
            await Db.SetRemoveAsync(MessageIndex(recipientDeviceId), staleIds.ToArray());
        }

        return records.OrderBy(r => r.SentAt).ToList();
    }

    /// <summary>
    /// Кладёт инвайт в буфер. Возвращает (inviteId, expiresAt).
    /// </summary>
    public virtual async Task<(string InviteId, DateTime ExpiresAt)> EnqueueInviteAsync(
        long senderUserId,
        Guid senderDeviceId,
        long recipientUserId,
        Guid recipientDeviceId,
        byte[] initialEnvelope)
    {
        var inviteId = Guid.NewGuid().ToString();
        var sentAt = DateTime.UtcNow;
        var record = new SecretInviteRecord
        {
            InviteId = inviteId,
            SenderUserId = senderUserId,
            SenderDeviceId = senderDeviceId,
            RecipientUserId = recipientUserId,
            RecipientDeviceId = recipientDeviceId,
            InitialEnvelope = initialEnvelope,
            SentAt = sentAt,
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(record);

        var batch = Db.CreateBatch();
        var setTask = batch.StringSetAsync(InviteKey(recipientDeviceId, inviteId), payload, DefaultTtl);
        var indexTask = batch.SetAddAsync(InviteIndex(recipientDeviceId), inviteId);
        var indexExpireTask = batch.KeyExpireAsync(InviteIndex(recipientDeviceId), DefaultTtl + TimeSpan.FromMinutes(5));
        batch.Execute();
        await Task.WhenAll(setTask, indexTask, indexExpireTask);

        var expiresAt = sentAt.Add(DefaultTtl);
        _logger.LogDebug(
            "Запись секретного инвайта {InviteId} для устройства {RecipientDeviceId} (sender={SenderUserId})",
            inviteId, recipientDeviceId, senderUserId);

        return (inviteId, expiresAt);
    }

    /// <summary>
    /// Получить и удалить инвайт по (recipientDeviceId, inviteId). Возвращает запись либо null если истёк.
    /// </summary>
    public virtual async Task<SecretInviteRecord?> ConsumeInviteAsync(Guid recipientDeviceId, string inviteId)
    {
        var key = InviteKey(recipientDeviceId, inviteId);
        var value = await Db.StringGetAsync(key);

        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var batch = Db.CreateBatch();
        var deleteTask = batch.KeyDeleteAsync(key);
        var unindexTask = batch.SetRemoveAsync(InviteIndex(recipientDeviceId), inviteId);
        batch.Execute();
        await Task.WhenAll(deleteTask, unindexTask);

        try
        {
            return JsonSerializer.Deserialize<SecretInviteRecord>((byte[])value!);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Не удалось десериализовать запись инвайта {InviteId}", inviteId);
            return null;
        }
    }

    public virtual async Task<List<SecretInviteRecord>> ListPendingInvitesAsync(Guid recipientDeviceId)
    {
        var ids = await Db.SetMembersAsync(InviteIndex(recipientDeviceId));
        if (ids.Length == 0)
        {
            return new List<SecretInviteRecord>();
        }

        var keys = ids.Select(id => (RedisKey)InviteKey(recipientDeviceId, id!)).ToArray();
        var values = await Db.StringGetAsync(keys);

        var records = new List<SecretInviteRecord>(values.Length);
        var staleIds = new List<RedisValue>();

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].IsNullOrEmpty)
            {
                staleIds.Add(ids[i]);
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<SecretInviteRecord>((byte[])values[i]!);
                if (record != null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Не удалось десериализовать запись инвайта {InviteId}", ids[i]);
                staleIds.Add(ids[i]);
            }
        }

        if (staleIds.Count > 0)
        {
            await Db.SetRemoveAsync(InviteIndex(recipientDeviceId), staleIds.ToArray());
        }

        return records.OrderBy(r => r.SentAt).ToList();
    }
}
