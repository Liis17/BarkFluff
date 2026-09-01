using BarkFluff.Setup.Setup;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace BarkFluff.Setup.Tests;

public sealed class SetupSessionStoreTests
{
    private static readonly SetupOptions Options = new(
        7032,
        new Uri("http://settings:7003"),
        "a-test-setup-token-with-enough-entropy",
        "https://setup.example.com",
        TimeSpan.FromHours(1));

    [Fact]
    public void Valid_token_creates_session_and_csrf_requires_same_origin()
    {
        var store = new SetupSessionStore(Options);
        Assert.True(store.TryLogin(Options.Secret, "198.51.100.4", out var session, out _));
        Assert.NotNull(session);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("setup.example.com");
        context.Request.Headers.Origin = Options.PublicOrigin;
        context.Request.Headers[SetupSessionStore.CsrfHeaderName] = session!.CsrfToken;

        Assert.True(store.ValidateMutation(context, session));

        context.Request.Headers.Origin = "https://attacker.example";
        Assert.False(store.ValidateMutation(context, session));
    }

    [Fact]
    public void Six_failed_attempts_are_rate_limited_per_address()
    {
        var store = new SetupSessionStore(Options);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.False(store.TryLogin("wrong-token", "198.51.100.5", out _, out var retryAfter));
            Assert.Equal(TimeSpan.Zero, retryAfter);
        }

        Assert.False(store.TryLogin("wrong-token", "198.51.100.5", out _, out var blockedRetry));
        Assert.True(blockedRetry > TimeSpan.Zero);
        Assert.False(store.TryLogin(Options.Secret, "198.51.100.5", out _, out var stillBlockedRetry));
        Assert.True(stillBlockedRetry > TimeSpan.Zero);
    }

    [Fact]
    public void Session_cookie_is_required_and_expiration_is_sliding()
    {
        var store = new SetupSessionStore(Options);
        Assert.True(store.TryLogin(Options.Secret, "198.51.100.6", out var session, out _));

        var context = new DefaultHttpContext();
        Assert.False(store.TryGet(context, out _));
        context.Request.Headers.Cookie = $"{SetupSessionStore.SessionCookieName}={session!.Id}";
        Assert.True(store.TryGet(context, out var current));
        Assert.True(current.ExpiresAtUtc > current.CreatedAtUtc);
    }
}
