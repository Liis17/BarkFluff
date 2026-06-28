using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.AddDraftUser;
using BarkFluff.Users.Features.ConfirmUser;
using BarkFluff.Users.Features.OverrideDraftUser;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Users.Tests.Features.UserLifecycle;

public class OverrideDraftUserCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingDraftByEmail_OverridesData()
    {
        var user = await _h.SeedUser(username: "olduser", email: "draft@test.com", isDraft: true);
        var handler = new OverrideDraftUserCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<OverrideDraftUserCommandHandler>());

        var result = await handler.Handle(new OverrideDraftUserCommand
        {
            Username = "newuser",
            FirstName = "New",
            LastName = "User",
            Email = "draft@test.com"
        }, CancellationToken.None);

        result.UserId.Should().Be(user.Id);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Username.Should().Be("newuser");
        updated.FirstName.Should().Be("New");
        updated.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingDraftByUsername_OverridesData()
    {
        var user = await _h.SeedUser(username: "draftuser", email: "old@test.com", isDraft: true);
        var handler = new OverrideDraftUserCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<OverrideDraftUserCommandHandler>());

        var result = await handler.Handle(new OverrideDraftUserCommand
        {
            Username = "draftuser",
            FirstName = "New",
            LastName = "Name",
            Email = "new@test.com"
        }, CancellationToken.None);

        result.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var handler = new OverrideDraftUserCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<OverrideDraftUserCommandHandler>());

        var act = () => handler.Handle(new OverrideDraftUserCommand
        {
            Username = "nonexistent",
            FirstName = "New",
            LastName = "User",
            Email = "nonexistent@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
