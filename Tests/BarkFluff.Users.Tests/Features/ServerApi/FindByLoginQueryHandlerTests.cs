using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Features.FindByLogin;
using BarkFluff.Users.Features.ListByIds;
using BarkFluff.Users.Features.GetUserByUsername;
using BarkFluff.Users.Features.SetProfilePictureServer;
using BarkFluff.Users.Features.UpdateProfileServer;
using BarkFluff.Users.Features.UpdateStorageLimit;
using FluentAssertions;
using Grpc.Core;
using Moq;

namespace BarkFluff.Users.Tests.Features.ServerApi;

public class FindByLoginQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ByUsername_ReturnsUser()
    {
        var user = await _h.SeedUser(username: "findme", email: "find@test.com");
        var handler = new FindByLoginQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<FindByLoginQueryHandler>());

        var result = await handler.Handle(new FindByLoginQuery { Username = "findme" }, CancellationToken.None);

        result.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task Handle_ByEmail_ReturnsUser()
    {
        var user = await _h.SeedUser(email: "find@test.com");
        var handler = new FindByLoginQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<FindByLoginQueryHandler>());

        var result = await handler.Handle(new FindByLoginQuery { Email = "find@test.com" }, CancellationToken.None);

        result.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task Handle_BothSet_EmailTakesPriority()
    {
        var user1 = await _h.SeedUser(username: "user1", email: "email1@test.com");
        var user2 = await _h.SeedUser(username: "user2", email: "email2@test.com");
        var handler = new FindByLoginQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<FindByLoginQueryHandler>());

        var result = await handler.Handle(new FindByLoginQuery { Username = "user1", Email = "email2@test.com" }, CancellationToken.None);

        result.User.Id.Should().Be(user2.Id);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsUserNotFoundException()
    {
        var handler = new FindByLoginQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<FindByLoginQueryHandler>());

        var act = () => handler.Handle(new FindByLoginQuery { Username = "nonexistent" }, CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    [Fact]
    public async Task Handle_EmptyUsernameAndEmail_ThrowsUserNotFoundException()
    {
        var handler = new FindByLoginQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<FindByLoginQueryHandler>());

        var act = () => handler.Handle(new FindByLoginQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    [Fact]
    public async Task Handle_BlockedUser_ReturnsRawProfileAndStatus()
    {
        var user = await _h.SeedUser(username: "blocked-login", firstName: "Real");
        user.AccountStatus = BarkFluff.Users.Domain.AccountStatus.Blocked;
        user.RestrictionReason = "reason";
        await _h.DbContext.SaveChangesAsync();
        var handler = new FindByLoginQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<FindByLoginQueryHandler>());

        var result = await handler.Handle(new FindByLoginQuery { Username = user.Username }, CancellationToken.None);

        result.User.FirstName.Should().Be("Real");
        result.User.Username.Should().Be("blocked-login");
        result.User.AccountStatus.Should().Be(BarkFluff.Proto.Users.AccountStatus.Blocked);
    }

    [Fact]
    public async Task Handle_DeletedUser_IsNotFound()
    {
        var user = await _h.SeedUser(username: "deleted-login");
        user.AccountStatus = BarkFluff.Users.Domain.AccountStatus.Deleted;
        await _h.DbContext.SaveChangesAsync();
        var handler = new FindByLoginQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<FindByLoginQueryHandler>());

        await FluentActions.Invoking(() => handler.Handle(new FindByLoginQuery { Username = user.Username }, CancellationToken.None))
            .Should().ThrowAsync<UserNotFoundException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
