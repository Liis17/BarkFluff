using BarkFluff.Users.Features.Privacy.GetPrivacySettings;
using BarkFluff.Users.Features.Privacy.GetUserPrivacyServer;
using BarkFluff.Users.Features.Privacy.UpdatePrivacySettings;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Privacy;

public class UpdatePrivacySettingsCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_UpdatesAllFields()
    {
        var user = await _h.SeedUser();
        await _h.SeedPrivacy(user.Id);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdatePrivacySettingsCommandHandler(ctx, _h.PrivacyStorage, TestHelper.CreateLogger<UpdatePrivacySettingsCommandHandler>());

        await handler.Handle(new UpdatePrivacySettingsCommand
        {
            Settings = new Proto.Users.PrivacySettings
            {
                ProfileVisibleOnSite = false,
                AvatarVisibility = Proto.Users.ProfileFieldVisibility.None,
                BioVisibility = Proto.Users.ProfileFieldVisibility.Friends,
                EmailVisibility = Proto.Users.ProfileFieldVisibility.All,
                SearchVisible = false,
                OnlineVisibility = Proto.Users.ProfileFieldVisibility.Friends,
            }
        }, CancellationToken.None);

        var updated = await _h.PrivacyStorage.Get(user.Id);
        updated!.ProfileVisibleOnSite.Should().BeFalse();
        updated.AvatarVisibility.Should().Be(Domain.ProfileFieldVisibility.None);
        updated.SearchVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoExistingPrivacy_CreatesNew()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdatePrivacySettingsCommandHandler(ctx, _h.PrivacyStorage, TestHelper.CreateLogger<UpdatePrivacySettingsCommandHandler>());

        await handler.Handle(new UpdatePrivacySettingsCommand
        {
            Settings = new Proto.Users.PrivacySettings { SearchVisible = false }
        }, CancellationToken.None);

        var privacy = await _h.PrivacyStorage.Get(user.Id);
        privacy.Should().NotBeNull();
        privacy!.SearchVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NullSettings_UsesDefaults()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdatePrivacySettingsCommandHandler(ctx, _h.PrivacyStorage, TestHelper.CreateLogger<UpdatePrivacySettingsCommandHandler>());

        await handler.Handle(new UpdatePrivacySettingsCommand { Settings = null }, CancellationToken.None);

        var privacy = await _h.PrivacyStorage.Get(user.Id);
        privacy.Should().NotBeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
