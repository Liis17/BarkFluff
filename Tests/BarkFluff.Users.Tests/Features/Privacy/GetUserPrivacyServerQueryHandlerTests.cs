using BarkFluff.Users.Features.Privacy.GetPrivacySettings;
using BarkFluff.Users.Features.Privacy.GetUserPrivacyServer;
using BarkFluff.Users.Features.Privacy.UpdatePrivacySettings;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Privacy;

public class GetUserPrivacyServerQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsPrivacyForUserId()
    {
        var user = await _h.SeedUser();
        await _h.SeedPrivacy(user.Id, searchVisible: false);
        var handler = new GetUserPrivacyServerQueryHandler(_h.PrivacyStorage, _h.UsersStorage);

        var result = await handler.Handle(new GetUserPrivacyServerQuery { UserId = user.Id }, CancellationToken.None);

        result.Settings.SearchVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoPrivacy_CreatesDefault()
    {
        var user = await _h.SeedUser();
        var handler = new GetUserPrivacyServerQueryHandler(_h.PrivacyStorage, _h.UsersStorage);

        var result = await handler.Handle(new GetUserPrivacyServerQuery { UserId = user.Id }, CancellationToken.None);

        result.Settings.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_RestrictedUser_HidesOnlineStatus()
    {
        var user = await _h.SeedUser();
        user.AccountStatus = Domain.AccountStatus.Blocked;
        await _h.SeedPrivacy(user.Id);
        var handler = new GetUserPrivacyServerQueryHandler(_h.PrivacyStorage, _h.UsersStorage);

        var result = await handler.Handle(new GetUserPrivacyServerQuery { UserId = user.Id }, CancellationToken.None);

        result.Settings.OnlineVisibility.Should().Be(BarkFluff.Proto.Users.ProfileFieldVisibility.None);
    }

    [Fact]
    public async Task Handle_DeletedUserDoesNotCreatePrivacy()
    {
        var user = await _h.SeedUser();
        user.AccountStatus = Domain.AccountStatus.Deleted;
        var handler = new GetUserPrivacyServerQueryHandler(_h.PrivacyStorage, _h.UsersStorage);

        var result = await handler.Handle(new GetUserPrivacyServerQuery { UserId = user.Id }, CancellationToken.None);

        result.Settings.OnlineVisibility.Should().Be(BarkFluff.Proto.Users.ProfileFieldVisibility.None);
        (await _h.PrivacyStorage.Get(user.Id)).Should().BeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
