using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Features.CheckExistUsername;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Users.Tests.Features.ExistenceChecks;

public class CheckExistEmailQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingNonDraftEmail_ReturnsExistTrue()
    {
        await _h.SeedUser(email: "existing@test.com", isDraft: false);
        var handler = new CheckExistEmailQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<CheckExistEmailQueryHandler>());

        var result = await handler.Handle(new CheckExistEmailQuery { Email = "existing@test.com" }, CancellationToken.None);

        result.Exist.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DraftEmail_ReturnsExistFalse()
    {
        await _h.SeedUser(email: "draft@test.com", isDraft: true);
        var handler = new CheckExistEmailQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<CheckExistEmailQueryHandler>());

        var result = await handler.Handle(new CheckExistEmailQuery { Email = "draft@test.com" }, CancellationToken.None);

        result.Exist.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NonExistingEmail_ReturnsExistFalse()
    {
        var handler = new CheckExistEmailQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<CheckExistEmailQueryHandler>());

        var result = await handler.Handle(new CheckExistEmailQuery { Email = "nonexistent@test.com" }, CancellationToken.None);

        result.Exist.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CaseInsensitive_ReturnsExistTrue()
    {
        await _h.SeedUser(email: "Test@Test.com", isDraft: false);
        var handler = new CheckExistEmailQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<CheckExistEmailQueryHandler>());

        var result = await handler.Handle(new CheckExistEmailQuery { Email = "test@test.com" }, CancellationToken.None);

        result.Exist.Should().BeTrue();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
