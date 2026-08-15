using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

using BarkFluff.FastAuth.Domain;
using BarkFluff.Proto.FastAuth;

using Google.Protobuf.WellKnownTypes;

using StackExchange.Redis;

namespace BarkFluff.FastAuth.Infrastructure;

/// <summary>
/// Доставка событий сессии стриму ожидающего клиента (см. docs/scaling/fastauth.md):
/// переход (Scan/Accept/Reject) выполняется на одном инстансе, а стрим нового устройства
/// открыт на другом. Публикация в Redis pub/sub канал + локальный реестр ожидающих:
/// стрим живёт ровно на одном инстансе, туда событие и придёт, остальные — no-op.
/// </summary>
public sealed class FastAuthEventBus(
    IConnectionMultiplexer redis,
    ILogger<FastAuthEventBus> logger) : BackgroundService, IFastAuthEventBus
{
    public const string ChannelName = "fastauth:events";

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Default;

    private readonly ConcurrentDictionary<string, Channel<FastAuthResult>> _waiters = new();

    public async Task PublishAsync(string sessionId, FastAuthResult result, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(EventDto.From(sessionId, result), Json);
        await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(ChannelName), payload);
    }

    public ChannelReader<FastAuthResult>? Attach(string sessionId)
    {
        var channel = Channel.CreateUnbounded<FastAuthResult>(
            new UnboundedChannelOptions { SingleReader = true });

        return _waiters.TryAdd(sessionId, channel) ? channel.Reader : null;
    }

    public void Detach(string sessionId)
    {
        if (_waiters.TryRemove(sessionId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(ChannelName), OnMessage);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnMessage(RedisChannel channel, RedisValue message)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<EventDto>(message.ToString(), Json);
            if (evt is null)
            {
                return;
            }

            if (_waiters.TryGetValue(evt.SessionId, out var waiter))
            {
                waiter.Writer.TryWrite(evt.ToProto());

                if (evt.IsFinal)
                {
                    waiter.Writer.TryComplete();
                }
            }
        }
        catch (Exception ex)
        {
            // Колбэк SE.Redis нельзя ронять: пропущенное событие покрывается
            // перечитыванием стора и локальным дедлайном в подписчике.
            logger.LogWarning(ex, "Failed to dispatch FastAuth event");
        }
    }

    /// <summary>Wire-формат события pub/sub.</summary>
    private sealed record EventDto(
        string SessionId,
        int Status,
        string? AccessToken,
        long? AccessTokenExpiresAtMs,
        string? RefreshToken,
        long? RefreshTokenExpiresAtMs)
    {
        public bool IsFinal => Status is >= (int)FastAuthStatus.Accepted and <= (int)FastAuthStatus.Expired;

        public static EventDto From(string sessionId, FastAuthResult r) => new(
            sessionId,
            (int)r.Status,
            string.IsNullOrEmpty(r.AccessToken) ? null : r.AccessToken,
            ToUnixMs(r.AccessTokenExpiresAt),
            string.IsNullOrEmpty(r.RefreshToken) ? null : r.RefreshToken,
            ToUnixMs(r.RefreshTokenExpiresAt));

        public FastAuthResult ToProto() => new()
        {
            Status = (FastAuthStatus)Status,
            AccessToken = AccessToken ?? string.Empty,
            AccessTokenExpiresAt = FromUnixMs(AccessTokenExpiresAtMs),
            RefreshToken = RefreshToken ?? string.Empty,
            RefreshTokenExpiresAt = FromUnixMs(RefreshTokenExpiresAtMs)
        };

        private static long? ToUnixMs(Timestamp? value) =>
            value is null ? null : new DateTimeOffset(value.ToDateTime()).ToUnixTimeMilliseconds();

        private static Timestamp? FromUnixMs(long? value) =>
            value is null ? null
                : Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(value.Value).UtcDateTime);
    }
}
