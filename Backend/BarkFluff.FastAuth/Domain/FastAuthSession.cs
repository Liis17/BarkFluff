using System.Threading.Channels;

using BarkFluff.Proto.FastAuth;

namespace BarkFluff.FastAuth.Domain;

public sealed class FastAuthSession
{
    private readonly object _gate = new();
    private readonly Channel<FastAuthResult> _events = Channel.CreateUnbounded<FastAuthResult>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private bool _hasSubscriber;

    public required string Id { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string DeviceName { get; init; }
    public required string OperationSystem { get; init; }
    public required string AppName { get; init; }
    public required string AppVersion { get; init; }
    public required string IpAddress { get; init; }

    public FastAuthStatus Status { get; private set; } = FastAuthStatus.Pending;
    public string? ConfirmationCode { get; private set; }
    public long? UserId { get; private set; }
    public DateTime? FinalizedAt { get; private set; }

    public ChannelReader<FastAuthResult> Events => _events.Reader;

    public bool IsFinal =>
        Status is FastAuthStatus.Accepted or FastAuthStatus.Rejected or FastAuthStatus.Expired;

    /// <summary>
    /// Закрепляет единственного подписчика стрима за сессией. Возвращает false,
    /// если подписчик уже закреплён — повторно подписаться нельзя.
    /// </summary>
    public bool TryAttachSubscriber()
    {
        lock (_gate)
        {
            if (_hasSubscriber)
            {
                return false;
            }

            _hasSubscriber = true;
            return true;
        }
    }

    public ScanOutcome TryScan(long userId)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow >= ExpiresAt)
            {
                return ScanOutcome.Expired;
            }

            if (Status != FastAuthStatus.Pending)
            {
                return ScanOutcome.AlreadyHandled;
            }

            UserId = userId;
            ConfirmationCode = Guid.NewGuid().ToString();
            Status = FastAuthStatus.Scanned;
            _events.Writer.TryWrite(new FastAuthResult { Status = FastAuthStatus.Scanned });
            return ScanOutcome.Ok;
        }
    }

    public bool TryAccept(string confirmationCode, long userId, FastAuthResult acceptedResult)
    {
        lock (_gate)
        {
            if (Status != FastAuthStatus.Scanned) return false;
            if (ConfirmationCode != confirmationCode) return false;
            if (UserId != userId) return false;

            Status = FastAuthStatus.Accepted;
            FinalizedAt = DateTime.UtcNow;
            _events.Writer.TryWrite(acceptedResult);
            _events.Writer.TryComplete();
            return true;
        }
    }

    public bool TryReject(string confirmationCode, long userId)
    {
        lock (_gate)
        {
            if (Status != FastAuthStatus.Scanned) return false;
            if (ConfirmationCode != confirmationCode) return false;
            if (UserId != userId) return false;

            Status = FastAuthStatus.Rejected;
            FinalizedAt = DateTime.UtcNow;
            _events.Writer.TryWrite(new FastAuthResult { Status = FastAuthStatus.Rejected });
            _events.Writer.TryComplete();
            return true;
        }
    }

    public bool TryExpire()
    {
        lock (_gate)
        {
            if (IsFinal) return false;

            Status = FastAuthStatus.Expired;
            FinalizedAt = DateTime.UtcNow;
            _events.Writer.TryWrite(new FastAuthResult { Status = FastAuthStatus.Expired });
            _events.Writer.TryComplete();
            return true;
        }
    }
}

public enum ScanOutcome
{
    Ok,
    Expired,
    AlreadyHandled
}
