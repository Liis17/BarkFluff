using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Identity.Tests.Persistence;

public class AuthPropertiesStorageTests : PersistenceTestBase
{
    [Fact]
    public async Task CheckOtpEnabled_NoProperties_ReturnsFalse()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        var result = await storage.CheckOtpEnabled(1);

        Assert.False(result);
    }

    [Fact]
    public async Task CheckOtpEnabled_OtpDisabled_ReturnsFalse()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = false });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        var result = await storage.CheckOtpEnabled(1);

        Assert.False(result);
    }

    [Fact]
    public async Task CheckOtpEnabled_OtpEnabled_ReturnsTrue()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        var result = await storage.CheckOtpEnabled(1);

        Assert.True(result);
    }

    [Fact]
    public async Task AddUserOtpSecretKey_NewUser_CreatesProperties()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.AddUserOtpSecretKey(1, "SECRETKEY");

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.Equal("SECRETKEY", props.OtpSecret);
        Assert.False(props.OtpEnabled);
    }

    [Fact]
    public async Task AddUserOtpSecretKey_ExistingUser_UpdatesSecret()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpSecret = "OLD" });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.AddUserOtpSecretKey(1, "NEW");

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.Equal("NEW", props.OtpSecret);
    }

    [Fact]
    public async Task GetOtpSecretKey_Exists_ReturnsSecret()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpSecret = "MYKEY" });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        var result = await storage.GetOtpSecretKey(1);

        Assert.Equal("MYKEY", result);
    }

    [Fact]
    public async Task GetOtpSecretKey_NotFound_ThrowsOtpNotCreatedException()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        await Assert.ThrowsAsync<OtpNotCreatedException>(() => storage.GetOtpSecretKey(999));
    }

    [Fact]
    public async Task EnableOtp_SetsOtpEnabledTrue()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = false });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.EnableOtp(1);

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.True(props.OtpEnabled);
    }

    [Fact]
    public async Task EnableOtp_NotFound_ThrowsOtpNotCreatedException()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        await Assert.ThrowsAsync<OtpNotCreatedException>(() => storage.EnableOtp(999));
    }

    [Fact]
    public async Task EnableEmailOtp_SetsEmailOtpEnabledTrue()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, EmailOtpEnabled = false });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.EnableEmailOtp(1);

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.True(props.EmailOtpEnabled);
    }

    [Fact]
    public async Task DisableOtp_SetsOtpEnabledFalse()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.DisableOtp(1);

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.OtpEnabled);
    }

    [Fact]
    public async Task DisableOtp_NotFound_ThrowsOtpNotCreatedException()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        await Assert.ThrowsAsync<OtpNotCreatedException>(() => storage.DisableOtp(999));
    }

    [Fact]
    public async Task DisableEmailOtp_SetsEmailOtpEnabledFalse()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, EmailOtpEnabled = true });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.DisableEmailOtp(1);

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.EmailOtpEnabled);
    }

    [Fact]
    public async Task GetUserAuthProperties_Exists_ReturnsProperties()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        var result = await storage.GetUserAuthProperties(1);

        Assert.NotNull(result);
        Assert.True(result.OtpEnabled);
    }

    [Fact]
    public async Task GetUserAuthProperties_NotFound_ReturnsNull()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        var result = await storage.GetUserAuthProperties(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateLastEmailAuthCode_NewUser_CreatesProperties()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.UpdateLastEmailAuthCode(1, "123456");

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.Equal("123456", props.LastEmailAuthCode);
    }

    [Fact]
    public async Task UpdateLastEmailAuthCode_ExistingUser_UpdatesCode()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, LastEmailAuthCode = "OLD" });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.UpdateLastEmailAuthCode(1, "NEW");

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.Equal("NEW", props.LastEmailAuthCode);
    }

    [Fact]
    public async Task UpdateOptType_SetsSelectedOtpType()
    {
        using var ctx = CreateContext();
        ctx.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, SelectedOtpType = OtpType.Unknown });
        await ctx.SaveChangesAsync();
        var storage = new AuthPropertiesStorage(ctx);

        await storage.UpdateOptType(OtpType.Authenticator, 1);

        var props = await ctx.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.Equal(OtpType.Authenticator, props.SelectedOtpType);
    }

    [Fact]
    public async Task UpdateOptType_NotFound_ThrowsOtpNotCreatedException()
    {
        using var ctx = CreateContext();
        var storage = new AuthPropertiesStorage(ctx);

        await Assert.ThrowsAsync<OtpNotCreatedException>(() => storage.UpdateOptType(OtpType.Email, 999));
    }
}
