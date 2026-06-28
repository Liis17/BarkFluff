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

public class ChangeBioCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidBio_ChangesBio()
    {
        var user = await _h.SeedUser(bio: "Old bio");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeBioCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeBioCommandHandler>());

        await handler.Handle(new ChangeBioCommand { Bio = "New bio" }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Bio.Should().Be("New bio");
    }

    [Fact]
    public async Task Handle_BioOver200Chars_ThrowsBioTooLongException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeBioCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeBioCommandHandler>());

        var act = () => handler.Handle(new ChangeBioCommand { Bio = new string('x', 201) }, CancellationToken.None);

        await act.Should().ThrowAsync<BioTooLongException>();
    }

    [Fact]
    public async Task Handle_Exactly200Chars_Succeeds()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeBioCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeBioCommandHandler>());

        await handler.Handle(new ChangeBioCommand { Bio = new string('x', 200) }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Bio.Should().HaveLength(200);
    }

    [Fact]
    public async Task Handle_NullBio_SetsNull()
    {
        var user = await _h.SeedUser(bio: "Existing");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeBioCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeBioCommandHandler>());

        await handler.Handle(new ChangeBioCommand { Bio = null! }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Bio.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PublishesBioChangedEvent()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeBioCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeBioCommandHandler>());

        await handler.Handle(new ChangeBioCommand { Bio = "New bio" }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Users.UserChangedBio>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
