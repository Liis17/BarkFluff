using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Setup.Setup;

public sealed record SetupSession(
    string Id,
    string CsrfToken,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed class SetupSessionStore
{
    public const string SessionCookieName = "barkfluff_setup_session";
    public const string CsrfHeaderName = "X-CSRF-Token";

    private const int MaxLoginAttempts = 5;
    private static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(5);
    private readonly SetupOptions _options;
    private readonly byte[] _secretHash;
    private readonly ConcurrentDictionary<string, SetupSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LoginWindowState> _loginWindows = new(StringComparer.Ordinal);

    public SetupSessionStore(SetupOptions options)
    {
        _options = options;
        _secretHash = options.SecretHash;
    }

    public bool TryLogin(string? candidate, string clientAddress, out SetupSession? session, out TimeSpan retryAfter)
    {
        CleanupExpired();
        session = null;
        retryAfter = TimeSpan.Zero;

        var now = DateTimeOffset.UtcNow;
        if (!TryRegisterAttempt(clientAddress, now, out retryAfter))
            return false;

        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate?.Trim() ?? string.Empty));
        if (!CryptographicOperations.FixedTimeEquals(_secretHash, candidateHash))
            return false;

        _loginWindows.TryRemove(clientAddress, out _);
        session = new SetupSession(
            CreateToken(),
            CreateToken(),
            now,
            now,
            now.Add(_options.SessionLifetime));
        _sessions[session.Id] = session;
        return true;
    }

    public bool TryGet(HttpContext context, out SetupSession session)
    {
        session = null!;
        if (!context.Request.Cookies.TryGetValue(SessionCookieName, out var id)
            || string.IsNullOrWhiteSpace(id)
            || !_sessions.TryGetValue(id, out var current))
            return false;

        var now = DateTimeOffset.UtcNow;
        if (current.ExpiresAtUtc <= now)
        {
            _sessions.TryRemove(id, out _);
            return false;
        }

        session = current with { LastSeenAtUtc = now, ExpiresAtUtc = now.Add(_options.SessionLifetime) };
        _sessions[id] = session;
        SetCookie(context, session);
        return true;
    }

    public void SetCookie(HttpContext context, SetupSession session)
    {
        context.Response.Cookies.Append(SessionCookieName, session.Id, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = _options.SessionLifetime
        });
    }

    public void Clear(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(SessionCookieName, out var id))
            _sessions.TryRemove(id, out _);

        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict });
    }

    public bool ValidateMutation(HttpContext context, SetupSession session)
    {
        var candidate = context.Request.Headers[CsrfHeaderName].FirstOrDefault();
        if (!FixedTimeEquals(candidate, session.CsrfToken))
            return false;

        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origin))
            return true;

        var expected = _options.PublicOrigin
            ?? $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
        return string.Equals(origin.TrimEnd('/'), expected, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryRegisterAttempt(string address, DateTimeOffset now, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        while (true)
        {
            var current = _loginWindows.GetOrAdd(address, _ => new LoginWindowState(now, 0));
            if (now - current.StartedAtUtc >= LoginWindow)
            {
                var reset = new LoginWindowState(now, 0);
                if (_loginWindows.TryUpdate(address, reset, current))
                    continue;
                continue;
            }

            if (current.Attempts >= MaxLoginAttempts)
            {
                retryAfter = current.StartedAtUtc.Add(LoginWindow) - now;
                return false;
            }

            var next = current with { Attempts = current.Attempts + 1 };
            if (_loginWindows.TryUpdate(address, next, current))
                return true;
        }
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresAtUtc <= now)
                _sessions.TryRemove(pair.Key, out _);
        }

        foreach (var pair in _loginWindows)
        {
            if (now - pair.Value.StartedAtUtc >= LoginWindow)
                _loginWindows.TryRemove(pair.Key, out _);
        }
    }

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (left is null)
            return false;

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record LoginWindowState(DateTimeOffset StartedAtUtc, int Attempts);
}
