using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Identity.Tests.Persistence;

public class RefreshTokensStorageTests : PersistenceTestBase
{
    [Fact]
    public async Task CreateNewRefreshToken_CreatesAndReturnsToken()
    {
        using var ctx = CreateContext();
        var storage = new RefreshTokensStorage(ctx);

        var result = await storage.CreateNewRefreshToken("token123", 1, "dev-1", 30);

        Assert.NotNull(result);
        Assert.Equal("token123", result.Value);
        Assert.Equal(1, result.UserId);
        Assert.Equal("dev-1", result.DeviceId);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task FindRefreshToken_Exists_ReturnsToken()
    {
        using var ctx = CreateContext();
        ctx.RefreshTokens.Add(new RefreshToken { Value = "findme", UserId = 1, DeviceId = "d1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await ctx.SaveChangesAsync();
        var storage = new RefreshTokensStorage(ctx);

        var result = await storage.FindRefreshToken("findme");

        Assert.NotNull(result);
        Assert.Equal("findme", result.Value);
    }

    [Fact]
    public async Task FindRefreshToken_NotFound_ReturnsNull()
    {
        using var ctx = CreateContext();
        var storage = new RefreshTokensStorage(ctx);

        var result = await storage.FindRefreshToken("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRefreshTokens_ReturnsAllForUser()
    {
        using var ctx = CreateContext();
        ctx.RefreshTokens.Add(new RefreshToken { Value = "t1", UserId = 1, DeviceId = "d1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        ctx.RefreshTokens.Add(new RefreshToken { Value = "t2", UserId = 1, DeviceId = "d2", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        ctx.RefreshTokens.Add(new RefreshToken { Value = "t3", UserId = 2, DeviceId = "d3", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await ctx.SaveChangesAsync();
        var storage = new RefreshTokensStorage(ctx);

        var result = await storage.GetRefreshTokens(1);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task DeleteRefreshTokensByDeviceId_Existing_DeletesTokens()
    {
        using var ctx = CreateContext();
        ctx.RefreshTokens.Add(new RefreshToken { Value = "t1", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        ctx.RefreshTokens.Add(new RefreshToken { Value = "t2", UserId = 1, DeviceId = "dev2", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await ctx.SaveChangesAsync();
        var storage = new RefreshTokensStorage(ctx);

        await storage.DeleteRefreshTokensByDeviceId("dev1", 1);

        var remaining = await storage.GetRefreshTokens(1);
        Assert.Single(remaining);
        Assert.Equal("t2", remaining[0].Value);
    }

    [Fact]
    public async Task DeleteRefreshTokensByDeviceId_NotFound_ThrowsRefreshTokenNotFoundException()
    {
        using var ctx = CreateContext();
        var storage = new RefreshTokensStorage(ctx);

        await Assert.ThrowsAsync<RefreshTokenNotFoundException>(() => storage.DeleteRefreshTokensByDeviceId("dev1", 1));
    }

    [Fact]
    public async Task DeleteRefreshTokensByDeviceIdSafe_Existing_DeletesTokens()
    {
        using var ctx = CreateContext();
        ctx.RefreshTokens.Add(new RefreshToken { Value = "t1", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await ctx.SaveChangesAsync();
        var storage = new RefreshTokensStorage(ctx);

        await storage.DeleteRefreshTokensByDeviceIdSafe("dev1", 1);

        var remaining = await storage.GetRefreshTokens(1);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteRefreshTokensByDeviceIdSafe_NotFound_DoesNotThrow()
    {
        using var ctx = CreateContext();
        var storage = new RefreshTokensStorage(ctx);

        await storage.DeleteRefreshTokensByDeviceIdSafe("dev1", 1);

        Assert.Empty(ctx.RefreshTokens);
    }
}
