using BarkFluff.Users.Features.OverrideDraftUser;
using FluentAssertions;
using Grpc.Core;

namespace BarkFluff.Users.Tests.Features.UserLifecycle;

public class OverrideDraftUserCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingDraftByEmail_RefusesAndDoesNotChangeData()
    {
        var user = await _h.SeedUser(username: "olduser", email: "draft@test.com", isDraft: true);
        var handler = new OverrideDraftUserCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<OverrideDraftUserCommandHandler>());

        var act = () => handler.Handle(new OverrideDraftUserCommand
        {
            Username = "newuser",
            FirstName = "New",
            LastName = "User",
            Email = "draft@test.com"
        }, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.Username.Should().Be("olduser");
        updated.FirstName.Should().Be("Test");
        updated.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingDraftByUsername_RefusesAndDoesNotChangeData()
    {
        var user = await _h.SeedUser(username: "draftuser", email: "old@test.com", isDraft: true);
        var handler = new OverrideDraftUserCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<OverrideDraftUserCommandHandler>());

        var act = () => handler.Handle(new OverrideDraftUserCommand
        {
            Username = "draftuser",
            FirstName = "New",
            LastName = "Name",
            Email = "new@test.com"
        }, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);

        var unchanged = await _h.UsersStorage.GetById(user.Id);
        unchanged!.Username.Should().Be("draftuser");
        unchanged.FirstName.Should().Be("Test");
        unchanged.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UserNotFound_RefusesWithoutCreatingAccount()
    {
        var handler = new OverrideDraftUserCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<OverrideDraftUserCommandHandler>());

        var act = () => handler.Handle(new OverrideDraftUserCommand
        {
            Username = "nonexistent",
            FirstName = "New",
            LastName = "User",
            Email = "nonexistent@test.com"
        }, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        (await _h.UsersStorage.GetUserByUsername("nonexistent")).Should().BeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
