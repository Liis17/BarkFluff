using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.Federation.Tests.Services;

public class ActiveSigningKeyCacheTests
{
    [Fact]
    public void Current_BeforeRefresh_IsNull()
    {
        using var provider = TestHelpers.CreateProvider(TestHelpers.CreateDatabase());
        var cache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());

        cache.Current.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_LoadsActiveKeyFromDatabase()
    {
        var db = TestHelpers.CreateDatabase();
        await using (var seedContext = TestHelpers.CreateContext(db))
        {
            await TestHelpers.EnsureActiveKeyAsync(seedContext);
        }

        using var provider = TestHelpers.CreateProvider(db);
        var cache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());

        await cache.RefreshAsync();

        cache.Current.Should().NotBeNull();
        cache.Current!.KeyId.Should().Be("ed25519:1");
        cache.Current.PrivateKeySeed.Should().HaveCount(32);
    }

    [Fact]
    public async Task RefreshAsync_AfterRotation_PicksNewestKey()
    {
        var db = TestHelpers.CreateDatabase();
        await using (var seedContext = TestHelpers.CreateContext(db))
        {
            var keyService = TestHelpers.CreateSigningKeyService(seedContext);
            await keyService.EnsureActiveKeyAsync();
            await keyService.RotateAsync();
        }

        using var provider = TestHelpers.CreateProvider(db);
        var cache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());

        await cache.RefreshAsync();

        cache.Current!.KeyId.Should().Be("ed25519:2");
    }

    [Fact]
    public async Task RefreshAsync_NoActiveKey_Throws()
    {
        using var provider = TestHelpers.CreateProvider(TestHelpers.CreateDatabase());
        var cache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => cache.RefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
