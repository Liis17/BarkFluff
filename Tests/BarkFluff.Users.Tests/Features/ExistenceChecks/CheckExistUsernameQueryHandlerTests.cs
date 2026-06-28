using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Features.CheckExistUsername;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Users.Tests.Features.ExistenceChecks;

public class CheckExistUsernameQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    private BarkFluff.Users.Services.ReservedUsernamesService CreateReservedService(string names = "admin,root")
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ReservedNames:Usernames"]).Returns(names);
        return new(config.Object);
    }

    [Fact]
    public async Task Handle_ExistingNonDraftUsername_ReturnsExistTrue()
    {
        await _h.SeedUser(username: "existinguser", isDraft: false);
        var handler = new CheckExistUsernameQueryHandler(
            _h.UsersStorage, CreateReservedService(), TestHelper.CreateLogger<CheckExistUsernameQueryHandler>());

        var result = await handler.Handle(new CheckExistUsernameQuery { Username = "existinguser" }, CancellationToken.None);

        result.Exist.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DraftUsername_ReturnsExistFalse()
    {
        await _h.SeedUser(username: "draftuser", isDraft: true);
        var handler = new CheckExistUsernameQueryHandler(
            _h.UsersStorage, CreateReservedService(), TestHelper.CreateLogger<CheckExistUsernameQueryHandler>());

        var result = await handler.Handle(new CheckExistUsernameQuery { Username = "draftuser" }, CancellationToken.None);

        result.Exist.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NonExistingUsername_ReturnsExistFalse()
    {
        var handler = new CheckExistUsernameQueryHandler(
            _h.UsersStorage, CreateReservedService(), TestHelper.CreateLogger<CheckExistUsernameQueryHandler>());

        var result = await handler.Handle(new CheckExistUsernameQuery { Username = "nonexistent" }, CancellationToken.None);

        result.Exist.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReservedUsername_ReturnsExistTrue()
    {
        var handler = new CheckExistUsernameQueryHandler(
            _h.UsersStorage, CreateReservedService("admin"), TestHelper.CreateLogger<CheckExistUsernameQueryHandler>());

        var result = await handler.Handle(new CheckExistUsernameQuery { Username = "admin" }, CancellationToken.None);

        result.Exist.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CaseInsensitive_ReturnsExistTrue()
    {
        await _h.SeedUser(username: "TestUser", isDraft: false);
        var handler = new CheckExistUsernameQueryHandler(
            _h.UsersStorage, CreateReservedService(), TestHelper.CreateLogger<CheckExistUsernameQueryHandler>());

        var result = await handler.Handle(new CheckExistUsernameQuery { Username = "testuser" }, CancellationToken.None);

        result.Exist.Should().BeTrue();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
