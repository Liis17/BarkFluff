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

public class SetProfilePictureServerCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_SetsAvatarFromUrl()
    {
        var user = await _h.SeedUser();
        var handler = new SetProfilePictureServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<SetProfilePictureServerCommandHandler>());

        await handler.Handle(new SetProfilePictureServerCommand
        {
            UserId = user.Id,
            ProfilePictureUrl = "https://admin.com/pic.png",
            ProfilePicturePreviewUrl = "https://admin.com/pic_small.png"
        }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.ProfilePicture.Should().Be("https://admin.com/pic.png");
    }

    [Fact]
    public async Task Handle_PublishesAvatarChangedEvent()
    {
        var user = await _h.SeedUser();
        var handler = new SetProfilePictureServerCommandHandler(
            _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<SetProfilePictureServerCommandHandler>());

        await handler.Handle(new SetProfilePictureServerCommand
        {
            UserId = user.Id,
            ProfilePictureUrl = "url",
            ProfilePicturePreviewUrl = "preview"
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Users.UserChangedAvatar>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
