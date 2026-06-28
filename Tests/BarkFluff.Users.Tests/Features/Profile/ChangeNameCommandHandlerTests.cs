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

public class ChangeNameCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidRequest_ChangesName()
    {
        var user = await _h.SeedUser(firstName: "Old", lastName: "Name");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeNameCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeNameCommandHandler>());

        await handler.Handle(new ChangeNameCommand { FirstName = "New", LastName = "Name" }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.FirstName.Should().Be("New");
        updated.LastName.Should().Be("Name");
    }

    [Fact]
    public async Task Handle_PublishesNameChangedEvent()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeNameCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeNameCommandHandler>());

        await handler.Handle(new ChangeNameCommand { FirstName = "New", LastName = "Name" }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Users.UserChangedName>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsWhitespace()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ChangeNameCommandHandler(ctx, _h.UsersStorage, _h.QueueSender, TestHelper.CreateLogger<ChangeNameCommandHandler>());

        await handler.Handle(new ChangeNameCommand { FirstName = "  New  ", LastName = "  Name  " }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.FirstName.Should().Be("New");
        updated.LastName.Should().Be("Name");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
