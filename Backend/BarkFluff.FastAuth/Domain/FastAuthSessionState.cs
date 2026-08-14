using BarkFluff.Proto.FastAuth;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.FastAuth.Domain;

/// <summary>
/// Неизменяемый снимок состояния QR-сессии. Источник истины — Redis
/// (любой инстанс видит и продвигает любую сессию, см. docs/scaling/fastauth.md).
/// </summary>
public sealed class FastAuthSessionState
{
    public required string Id { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string DeviceName { get; init; }
    public required string OperationSystem { get; init; }
    public required string AppName { get; init; }
    public required string AppVersion { get; init; }
    public required string IpAddress { get; init; }

    public FastAuthStatus Status { get; init; } = FastAuthStatus.Pending;
    public string? ConfirmationCode { get; init; }
    public long? UserId { get; init; }
    public DateTime? FinalizedAt { get; init; }
    public FastAuthSessionResult? Result { get; init; }

    public bool IsFinal =>
        Status is FastAuthStatus.Accepted or FastAuthStatus.Rejected or FastAuthStatus.Expired;
}

/// <summary>Результат сессии для стрима; при Accepted — с выпущенными токенами.</summary>
public sealed record FastAuthSessionResult(
    FastAuthStatus Status,
    string? AccessToken = null,
    DateTime? AccessTokenExpiresAt = null,
    string? RefreshToken = null,
    DateTime? RefreshTokenExpiresAt = null)
{
    public FastAuthResult ToProto() => new()
    {
        Status = Status,
        AccessToken = AccessToken ?? string.Empty,
        AccessTokenExpiresAt = AccessTokenExpiresAt.HasValue
            ? Timestamp.FromDateTime(AccessTokenExpiresAt.Value.ToUniversalTime())
            : null,
        RefreshToken = RefreshToken ?? string.Empty,
        RefreshTokenExpiresAt = RefreshTokenExpiresAt.HasValue
            ? Timestamp.FromDateTime(RefreshTokenExpiresAt.Value.ToUniversalTime())
            : null
    };
}

/// <summary>Результат атомарного перехода состояния сессии в сторе.</summary>
public enum FastAuthTransition
{
    Ok,
    NotFound,
    Expired,
    InvalidState
}

/// <summary>Тайминги жизненного цикла QR-сессии.</summary>
public static class FastAuthSessionTiming
{
    public static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);

    /// <summary>Сколько держим финализированную сессию в Redis, чтобы реконнект успел забрать результат.</summary>
    public static readonly TimeSpan FinalRetention = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Запас к TTL ключа сессии: после логического истечения значение ещё читаемо,
    /// поэтому сторе отличает Expired от NotFound.
    /// </summary>
    public static readonly TimeSpan ExpirySlack = TimeSpan.FromSeconds(30);
}
