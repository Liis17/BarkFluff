using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.AddDraftUser;
using BarkFluff.Users.Features.ConfirmUser;
using BarkFluff.Users.Features.OverrideDraftUser;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Users.Tests.Features.UserLifecycle;

public class AddDraftUserCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_WithoutEmail_CreatesDraftWithoutContact()
    {
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());
        var result = await handler.Handle(new AddDraftUserCommand
        {
            Username = "telegramuser", FirstName = "New", LastName = "User", Email = ""
        }, CancellationToken.None);
        var user = await _h.UsersStorage.GetById(result.UserId);
        user!.Contact.Should().BeNull();
        (await _h.UsersStorage.GetUserByEmail("")).Should().BeNull();
    }

    [Fact]
    public async Task OverrideDraft_DoesNotMutateExistingAccount()
    {
        var user = await _h.SeedUser(username: "existing", email: "old@test.com", isDraft: false);
        var handler = new OverrideDraftUserCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<OverrideDraftUserCommandHandler>());
        var act = () => handler.Handle(new OverrideDraftUserCommand
        {
            Username = "existing", Email = "attacker@test.com", FirstName = "Changed", LastName = ""
        }, CancellationToken.None);
        await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        var unchanged = await _h.UsersStorage.GetUserByUsername("existing");
        unchanged!.IsDraft.Should().BeFalse();
        unchanged.Contact!.Email.Should().Be("old@test.com");
    }

    [Fact]
    public async Task Handle_ValidData_CreatesDraftUser()
    {
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService("admin,root"), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var result = await handler.Handle(new AddDraftUserCommand
        {
            Username = "newuser",
            FirstName = "New",
            LastName = "User",
            Email = "new@test.com"
        }, CancellationToken.None);

        result.UserId.Should().BeGreaterThan(0);

        var user = await _h.UsersStorage.GetById(result.UserId);
        user.Should().NotBeNull();
        user!.IsDraft.Should().BeTrue();
        user.Username.Should().Be("newuser");
        user.FirstName.Should().Be("New");
        user.Contact.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task Handle_InvalidUsernameFormat_ThrowsUsernameInvalidFormatException()
    {
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var act = () => handler.Handle(new AddDraftUserCommand
        {
            Username = "ab",
            FirstName = "Test",
            LastName = "User",
            Email = "test@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UsernameInvalidFormatException>();
    }

    [Fact]
    public async Task Handle_EmailAlreadyExists_ThrowsEmailExistException()
    {
        await _h.SeedUser(email: "taken@test.com", isDraft: false);
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var act = () => handler.Handle(new AddDraftUserCommand
        {
            Username = "newuser",
            FirstName = "New",
            LastName = "User",
            Email = "taken@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<EmailExistException>();
    }

    [Fact]
    public async Task Handle_EmailBelongsToDraft_ThrowsUserIsDraftException()
    {
        await _h.SeedUser(email: "draft@test.com", isDraft: true);
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var act = () => handler.Handle(new AddDraftUserCommand
        {
            Username = "newuser",
            FirstName = "New",
            LastName = "User",
            Email = "draft@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UserIsDraftException>();
    }

    [Fact]
    public async Task Handle_UsernameAlreadyExists_ThrowsUsernameExistException()
    {
        await _h.SeedUser(username: "takenuser", isDraft: false);
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var act = () => handler.Handle(new AddDraftUserCommand
        {
            Username = "takenuser",
            FirstName = "New",
            LastName = "User",
            Email = "new@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UsernameExistException>();
    }

    [Fact]
    public async Task Handle_UsernameBelongsToDraft_ThrowsUserIsDraftException()
    {
        await _h.SeedUser(username: "draftuser", isDraft: true);
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var act = () => handler.Handle(new AddDraftUserCommand
        {
            Username = "draftuser",
            FirstName = "New",
            LastName = "User",
            Email = "new@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UserIsDraftException>();
    }

    [Fact]
    public async Task Handle_ReservedUsername_ThrowsUsernameReservedException()
    {
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService("admin,root"), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var act = () => handler.Handle(new AddDraftUserCommand
        {
            Username = "admin",
            FirstName = "New",
            LastName = "User",
            Email = "new@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UsernameReservedException>();
    }

    [Fact]
    public async Task Handle_TrimsWhitespace()
    {
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var result = await handler.Handle(new AddDraftUserCommand
        {
            Username = "  newuser  ",
            FirstName = "  New  ",
            LastName = "  User  ",
            Email = "  new@test.com  "
        }, CancellationToken.None);

        var user = await _h.UsersStorage.GetById(result.UserId);
        user!.Username.Should().Be("newuser");
        user.FirstName.Should().Be("New");
        user.Contact.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task Handle_CaseInsensitiveEmailCheck()
    {
        await _h.SeedUser(email: "Test@Test.com", isDraft: false);
        var handler = new AddDraftUserCommandHandler(
            _h.UsersStorage, _h.CreateReservedService(""), _h.Metrics, TestHelper.CreateLogger<AddDraftUserCommandHandler>());

        var act = () => handler.Handle(new AddDraftUserCommand
        {
            Username = "newuser",
            FirstName = "New",
            LastName = "User",
            Email = "test@test.com"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<EmailExistException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
