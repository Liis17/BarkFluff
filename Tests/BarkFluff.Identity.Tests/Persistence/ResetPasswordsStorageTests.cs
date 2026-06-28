using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Identity.Tests.Persistence;

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
