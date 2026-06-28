using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Identity.Tests.Persistence;

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
