using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Identity.Tests.Persistence;

public abstract class PersistenceTestBase
{
    protected IdentityContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityContext(options);
    }
}

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

public class ConfirmationCodesStorageTests : PersistenceTestBase
{
    [Fact]
    public async Task AddCode_SavesAndReturnsCode()
    {
        using var ctx = CreateContext();
        var storage = new ConfirmationCodesStorage(ctx);

        var code = new ConfirmationCode
        {
            Value = "123456",
            Expires = DateTime.UtcNow.AddHours(6),
            OwnerId = 1,
            Type = ConfirmationCodeType.Registration
        };

        var result = await storage.AddCode(code);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Single(ctx.ConfirmationCodes);
    }

    [Fact]
    public async Task GetCode_ExistingCode_ReturnsCode()
    {
        using var ctx = CreateContext();
        var code = new ConfirmationCode { Id = Guid.NewGuid(), Value = "123456", Type = ConfirmationCodeType.Registration };
        ctx.ConfirmationCodes.Add(code);
        await ctx.SaveChangesAsync();
        var storage = new ConfirmationCodesStorage(ctx);

        var result = await storage.GetCode(code.Id);

        Assert.NotNull(result);
        Assert.Equal("123456", result.Value);
    }

    [Fact]
    public async Task GetCode_NonExistingCode_ReturnsNull()
    {
        using var ctx = CreateContext();
        var storage = new ConfirmationCodesStorage(ctx);

        var result = await storage.GetCode(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteCode_ExistingCode_RemovesCode()
    {
        using var ctx = CreateContext();
        var code = new ConfirmationCode { Id = Guid.NewGuid(), Value = "123456" };
        ctx.ConfirmationCodes.Add(code);
        await ctx.SaveChangesAsync();
        var storage = new ConfirmationCodesStorage(ctx);

        await storage.DeleteCode(code.Id);

        Assert.Empty(ctx.ConfirmationCodes);
    }

    [Fact]
    public async Task DeleteCode_NonExistingCode_DoesNothing()
    {
        using var ctx = CreateContext();
        var storage = new ConfirmationCodesStorage(ctx);

        await storage.DeleteCode(Guid.NewGuid());

        Assert.Empty(ctx.ConfirmationCodes);
    }
}

public class PasswordsStorageTests : PersistenceTestBase
{
    [Fact]
    public async Task UpdateUserPasswordHash_NewUser_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var storage = new PasswordsStorage(ctx);

        var result = await storage.UpdateUserPasswordHash(1, "hash123");

        Assert.True(result);
        var pw = await ctx.UserPasswords.FirstAsync(x => x.UserId == 1);
        Assert.Equal("hash123", pw.PasswordHash);
    }

    [Fact]
    public async Task UpdateUserPasswordHash_ExistingUser_ReturnsFalse()
    {
        using var ctx = CreateContext();
        ctx.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = "old" });
        await ctx.SaveChangesAsync();
        var storage = new PasswordsStorage(ctx);

        var result = await storage.UpdateUserPasswordHash(1, "new");

        Assert.False(result);
        var pw = await ctx.UserPasswords.FirstAsync(x => x.UserId == 1);
        Assert.Equal("new", pw.PasswordHash);
    }

    [Fact]
    public async Task GetUserPasswordHash_Exists_ReturnsHash()
    {
        using var ctx = CreateContext();
        ctx.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = "myhash" });
        await ctx.SaveChangesAsync();
        var storage = new PasswordsStorage(ctx);

        var result = await storage.GetUserPasswordHash(1);

        Assert.Equal("myhash", result);
    }

    [Fact]
    public async Task GetUserPasswordHash_NotFound_ReturnsNull()
    {
        using var ctx = CreateContext();
        var storage = new PasswordsStorage(ctx);

        var result = await storage.GetUserPasswordHash(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClearUserPasswordHash_SetsHashToNull()
    {
        using var ctx = CreateContext();
        ctx.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = "myhash" });
        await ctx.SaveChangesAsync();
        var storage = new PasswordsStorage(ctx);

        await storage.ClearUserPasswordHash(1);

        var pw = await ctx.UserPasswords.FirstAsync(x => x.UserId == 1);
        Assert.Null(pw.PasswordHash);
    }

    [Fact]
    public async Task ClearUserPasswordHash_NotFound_DoesNothing()
    {
        using var ctx = CreateContext();
        var storage = new PasswordsStorage(ctx);

        await storage.ClearUserPasswordHash(999);

        Assert.Empty(ctx.UserPasswords);
    }
}

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

public class ResetPasswordsStorageTests : PersistenceTestBase
{
    [Fact]
    public async Task AddResetPassword_SavesAndReturnsEntity()
    {
        using var ctx = CreateContext();
        var storage = new ResetPasswordsStorage(ctx);

        var reset = new Domain.ResetPassword
        {
            UserId = 1,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            OtpType = OtpType.Authenticator,
            IsApproved = false
        };

        var result = await storage.AddResetPassword(reset);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Single(ctx.ResetPasswords);
    }

    [Fact]
    public async Task GetResetPassword_Exists_ReturnsEntity()
    {
        using var ctx = CreateContext();
        var id = Guid.NewGuid();
        ctx.ResetPasswords.Add(new Domain.ResetPassword { Id = id, UserId = 1, IsApproved = false });
        await ctx.SaveChangesAsync();
        var storage = new ResetPasswordsStorage(ctx);

        var result = await storage.GetResetPassword(id);

        Assert.NotNull(result);
        Assert.Equal(1, result.UserId);
    }

    [Fact]
    public async Task GetResetPassword_NotFound_ReturnsNull()
    {
        using var ctx = CreateContext();
        var storage = new ResetPasswordsStorage(ctx);

        var result = await storage.GetResetPassword(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task SetApproved_SetsFlagToTrue()
    {
        using var ctx = CreateContext();
        var id = Guid.NewGuid();
        ctx.ResetPasswords.Add(new Domain.ResetPassword { Id = id, UserId = 1, IsApproved = false });
        await ctx.SaveChangesAsync();
        var storage = new ResetPasswordsStorage(ctx);

        await storage.SetApproved(id);

        var entity = await storage.GetResetPassword(id);
        Assert.True(entity!.IsApproved);
    }
}
