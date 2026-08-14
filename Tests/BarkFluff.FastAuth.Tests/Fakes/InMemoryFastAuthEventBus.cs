using System.Collections.Concurrent;
using System.Threading.Channels;

using BarkFluff.FastAuth.Domain;
using BarkFluff.Proto.FastAuth;

namespace BarkFluff.FastAuth.Tests.Fakes;

/// <summary>
/// In-memory реализация шины событий: как FastAuthEventBus, но без Redis —
/// событие доставляется локально зарегистрированному ожидающему.
/// </summary>
public sealed class InMemoryFastAuthEventBus : IFastAuthEventBus
{
    private readonly ConcurrentDictionary<string, Channel<FastAuthResult>> _waiters = new();

    public Task PublishAsync(string sessionId, FastAuthResult result, CancellationToken ct = default)
    {
        if (_waiters.TryGetValue(sessionId, out var waiter))
        {
            waiter.Writer.TryWrite(result);

            if (result.Status is FastAuthStatus.Accepted or FastAuthStatus.Rejected or FastAuthStatus.Expired)
            {
                waiter.Writer.TryComplete();
            }
        }

        return Task.CompletedTask;
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
}
