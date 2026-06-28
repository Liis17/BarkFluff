using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.ChangeBio;
using BarkFluff.Users.Features.ChangeName;
using BarkFluff.Users.Features.ChangeUsername;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.GetUserContacts;
using BarkFluff.Users.Features.SetProfilePicture;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BarkFluff.Users.Tests.Features.Profile;

public class ChangeUsernameCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    private BarkFluff.Users.Services.ReservedUsernamesService ReservedService => CreateReservedService("admin,root");

    private BarkFluff.Users.Services.ReservedUsernamesService noReserved => CreateReservedService("");

    private static BarkFluff.Users.Services.ReservedUsernamesService CreateReservedService(string configValue)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ReservedNames:Usernames"]).Returns(configValue);
        return new BarkFluff.Users.Services.ReservedUsernamesService(config.Object);
    }

    [Fact]
    public async Task Handle_ValidUsername_ChangesUsername()
    {
        var user = await _h.SeedUser(username: "oldname");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeUsernameCommandHandler(ctx, _h.UsersStorage, ReservedService, _h.QueueSender, TestHelper.CreateLogger<ChangeUsernameCommandHandler>());

        await handler.Handle(new ChangeUsernameCommand { Username = "newname" }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Username.Should().Be("newname");
    }

    [Fact]
    public async Task Handle_InvalidFormat_ThrowsUsernameInvalidFormatException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var noReserved = CreateReservedService("");
        var handler = new ChangeUsernameCommandHandler(ctx, _h.UsersStorage, noReserved, _h.QueueSender, TestHelper.CreateLogger<ChangeUsernameCommandHandler>());

        var act = () => handler.Handle(new ChangeUsernameCommand { Username = "ab" }, CancellationToken.None);

        await act.Should().ThrowAsync<UsernameInvalidFormatException>();
    }

    [Fact]
    public async Task Handle_ReservedUsername_ThrowsUsernameReservedException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeUsernameCommandHandler(ctx, _h.UsersStorage, ReservedService, _h.QueueSender, TestHelper.CreateLogger<ChangeUsernameCommandHandler>());

        var act = () => handler.Handle(new ChangeUsernameCommand { Username = "admin" }, CancellationToken.None);

        await act.Should().ThrowAsync<UsernameReservedException>();
    }

    [Fact]
    public async Task Handle_AlreadyTakenUsername_ThrowsUsernameExistException()
    {
        await _h.SeedUser(username: "takenuser");
        var user = await _h.SeedUser(username: "currentuser");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeUsernameCommandHandler(ctx, _h.UsersStorage, ReservedService, _h.QueueSender, TestHelper.CreateLogger<ChangeUsernameCommandHandler>());

        var act = () => handler.Handle(new ChangeUsernameCommand { Username = "takenuser" }, CancellationToken.None);

        await act.Should().ThrowAsync<UsernameExistException>();
    }

    [Fact]
    public async Task Handle_SameUsernameAsCurrentUser_Succeeds()
    {
        var user = await _h.SeedUser(username: "myname");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeUsernameCommandHandler(ctx, _h.UsersStorage, ReservedService, _h.QueueSender, TestHelper.CreateLogger<ChangeUsernameCommandHandler>());

        await handler.Handle(new ChangeUsernameCommand { Username = "myname" }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Username.Should().Be("myname");
    }

    [Fact]
    public async Task Handle_PublishesUsernameChangedEvent()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeUsernameCommandHandler(ctx, _h.UsersStorage, ReservedService, _h.QueueSender, TestHelper.CreateLogger<ChangeUsernameCommandHandler>());

        await handler.Handle(new ChangeUsernameCommand { Username = "newname" }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Users.UserChangedUsername>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
