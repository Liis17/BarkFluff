using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.AddDraftUser;
using BarkFluff.Users.Features.ConfirmUser;
using BarkFluff.Users.Features.OverrideDraftUser;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Users.Tests.Features.UserLifecycle;

public class ConfirmUserCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidDraft_ConfirmsUser()
    {
        var user = await _h.SeedUser(isDraft: true);
        var handler = new ConfirmUserCommandHandler(_h.UsersStorage, _h.PrivacyStorage, TestHelper.CreateLogger<ConfirmUserCommandHandler>());

        await handler.Handle(new ConfirmUserCommand { UserId = user.Id }, CancellationToken.None);

        var confirmed = await _h.UsersStorage.GetById(user.Id);
        confirmed!.IsDraft.Should().BeFalse();

        var privacy = await _h.PrivacyStorage.Get(user.Id);
        privacy.Should().NotBeNull();
        privacy!.ProfileVisibleOnSite.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var handler = new ConfirmUserCommandHandler(_h.UsersStorage, _h.PrivacyStorage, TestHelper.CreateLogger<ConfirmUserCommandHandler>());

        var act = () => handler.Handle(new ConfirmUserCommand { UserId = 9999999 }, CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    [Fact]
    public async Task Handle_CreatesDefaultPrivacy()
    {
        var user = await _h.SeedUser(isDraft: true);
        var handler = new ConfirmUserCommandHandler(_h.UsersStorage, _h.PrivacyStorage, TestHelper.CreateLogger<ConfirmUserCommandHandler>());

        await handler.Handle(new ConfirmUserCommand { UserId = user.Id }, CancellationToken.None);

        var privacy = await _h.PrivacyStorage.Get(user.Id);
        privacy.Should().NotBeNull();
        privacy!.AvatarVisibility.Should().Be(Domain.ProfileFieldVisibility.All);
        privacy.EmailVisibility.Should().Be(Domain.ProfileFieldVisibility.None);
        privacy.SearchVisible.Should().BeTrue();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
