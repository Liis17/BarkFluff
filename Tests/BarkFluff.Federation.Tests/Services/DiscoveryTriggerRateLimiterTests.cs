using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Services;

public class DiscoveryTriggerRateLimiterTests
{
    [Fact]
    public void TryTrigger_FirstCallForServer_ReturnsTrue()
    {
        var limiter = new DiscoveryTriggerRateLimiter();

        limiter.TryTrigger("peer.test").Should().BeTrue();
    }

    [Fact]
    public void TryTrigger_RepeatWithinCooldown_ReturnsFalse()
    {
        var limiter = new DiscoveryTriggerRateLimiter();

        limiter.TryTrigger("peer.test").Should().BeTrue();
        limiter.TryTrigger("peer.test").Should().BeFalse();
    }

    [Fact]
    public void TryTrigger_DifferentServers_AreIndependent()
    {
        var limiter = new DiscoveryTriggerRateLimiter();

        limiter.TryTrigger("peer-a.test").Should().BeTrue();
        limiter.TryTrigger("peer-b.test").Should().BeTrue();
        limiter.TryTrigger("peer-a.test").Should().BeFalse();
    }
}
