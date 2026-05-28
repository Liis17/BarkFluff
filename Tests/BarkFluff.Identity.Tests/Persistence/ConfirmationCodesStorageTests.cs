using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Identity.Tests.Persistence;

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
