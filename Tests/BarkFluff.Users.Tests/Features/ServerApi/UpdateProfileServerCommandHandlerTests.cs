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

public class UpdateProfileServerCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_UpdatesChangedFieldsOnly()
    {
        var user = await _h.SeedUser(firstName: "Old", lastName: "Name", bio: "Old bio", username: "olduser");
        var handler = new UpdateProfileServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<UpdateProfileServerCommandHandler>());

        await handler.Handle(new UpdateProfileServerCommand
        {
            UserId = user.Id,
            FirstName = "New",
            LastName = "Name",
            Bio = "New bio"
        }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.FirstName.Should().Be("New");
    }

    [Fact]
    public async Task Handle_NoChanges_NoEventsPublished()
    {
        var user = await _h.SeedUser(firstName: "Same", lastName: "Name");
        var handler = new UpdateProfileServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<UpdateProfileServerCommandHandler>());

        await handler.Handle(new UpdateProfileServerCommand
        {
            UserId = user.Id,
            FirstName = "Same",
            LastName = "Name"
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidOperationException()
    {
        var handler = new UpdateProfileServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<UpdateProfileServerCommandHandler>());

        var act = () => handler.Handle(new UpdateProfileServerCommand { UserId = 9999999 }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_UsernameChanged_PublishesEvent()
    {
        var user = await _h.SeedUser(username: "oldname");
        var handler = new UpdateProfileServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<UpdateProfileServerCommandHandler>());

        await handler.Handle(new UpdateProfileServerCommand
        {
            UserId = user.Id,
            Username = "newname"
        }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Username.Should().Be("newname");
        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Users.UserChangedUsername>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_BioChanged_PublishesEvent()
    {
        var user = await _h.SeedUser(bio: "old bio");
        var handler = new UpdateProfileServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<UpdateProfileServerCommandHandler>());

        await handler.Handle(new UpdateProfileServerCommand
        {
            UserId = user.Id,
            Bio = "new bio"
        }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Bio.Should().Be("new bio");
        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Users.UserChangedBio>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NameChanged_PublishesEvent()
    {
        var user = await _h.SeedUser(firstName: "Old", lastName: "Name");
        var handler = new UpdateProfileServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<UpdateProfileServerCommandHandler>());

        await handler.Handle(new UpdateProfileServerCommand
        {
            UserId = user.Id,
            FirstName = "New",
            LastName = "Name"
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Users.UserChangedName>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
