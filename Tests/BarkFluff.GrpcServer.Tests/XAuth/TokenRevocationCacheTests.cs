using BarkFluff.GrpcServer.XAuth;

namespace BarkFluff.GrpcServer.Tests.XAuth;

public class TokenRevocationCacheTests
{
    [Fact]
    public void IsRevoked_SessionNeverRevoked_ReturnsFalse()
    {
        var cache = new TokenRevocationCache();

        cache.IsRevoked(1, "device-a").Should().BeFalse();
    }

    [Fact]
    public void IsRevoked_AfterRevoke_ReturnsTrue()
    {
        var cache = new TokenRevocationCache();

        cache.Revoke(1, "device-a", DateTime.UtcNow.AddMinutes(5));

        cache.IsRevoked(1, "device-a").Should().BeTrue();
    }

    [Fact]
    public void IsRevoked_ScopedToUserAndDevice()
    {
        var cache = new TokenRevocationCache();

        cache.Revoke(1, "device-a", DateTime.UtcNow.AddMinutes(5));

        cache.IsRevoked(1, "device-a").Should().BeTrue();
        cache.IsRevoked(1, "device-b").Should().BeFalse();
        cache.IsRevoked(2, "device-a").Should().BeFalse();
    }

    [Fact]
    public void Cleanup_RemovesExpiredRevocation()
    {
        var cache = new TokenRevocationCache();
        cache.Revoke(1, "device-a", DateTime.UtcNow.AddMinutes(-1));

        cache.Cleanup();

        cache.IsRevoked(1, "device-a").Should().BeFalse();
    }

    // Безопасность: Cleanup НЕ должен удалять отзыв, чей access-токен ещё не истёк,
    // иначе отозванная, но ещё живая сессия снова станет валидной.
    [Fact]
    public void Cleanup_KeepsStillValidRevocation()
    {
        var cache = new TokenRevocationCache();
        cache.Revoke(1, "device-a", DateTime.UtcNow.AddMinutes(5));

        cache.Cleanup();

        cache.IsRevoked(1, "device-a").Should().BeTrue();
    }

    [Fact]
    public void Cleanup_RemovesOnlyExpired_KeepsValid()
    {
        var cache = new TokenRevocationCache();
        cache.Revoke(1, "expired", DateTime.UtcNow.AddMinutes(-1));
        cache.Revoke(2, "valid", DateTime.UtcNow.AddMinutes(5));

        cache.Cleanup();

        cache.IsRevoked(1, "expired").Should().BeFalse();
        cache.IsRevoked(2, "valid").Should().BeTrue();
    }

    [Fact]
    public void Revoke_SameSessionAgain_UpdatesExpiry()
    {
        var cache = new TokenRevocationCache();
        cache.Revoke(1, "device-a", DateTime.UtcNow.AddMinutes(-1));

        // Повторный отзыв с новым (ещё живым) токеном продлевает срок записи.
        cache.Revoke(1, "device-a", DateTime.UtcNow.AddMinutes(5));
        cache.Cleanup();

        cache.IsRevoked(1, "device-a").Should().BeTrue();
    }
}
