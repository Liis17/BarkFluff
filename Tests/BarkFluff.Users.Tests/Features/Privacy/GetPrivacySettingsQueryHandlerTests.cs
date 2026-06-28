using BarkFluff.Users.Features.Privacy.GetPrivacySettings;
using BarkFluff.Users.Features.Privacy.GetUserPrivacyServer;
using BarkFluff.Users.Features.Privacy.UpdatePrivacySettings;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Privacy;

public class GetPrivacySettingsQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingPrivacy_ReturnsSettings()
    {
        var user = await _h.SeedUser();
        await _h.SeedPrivacy(user.Id);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetPrivacySettingsQueryHandler(ctx, _h.PrivacyStorage, TestHelper.CreateLogger<GetPrivacySettingsQueryHandler>());

        var result = await handler.Handle(new GetPrivacySettingsQuery(), CancellationToken.None);

        result.Settings.Should().NotBeNull();
        result.Settings.ProfileVisibleOnSite.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoPrivacy_CreatesDefault()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetPrivacySettingsQueryHandler(ctx, _h.PrivacyStorage, TestHelper.CreateLogger<GetPrivacySettingsQueryHandler>());

        var result = await handler.Handle(new GetPrivacySettingsQuery(), CancellationToken.None);

        result.Settings.Should().NotBeNull();
        result.Settings.EmailVisibility.Should().Be(Proto.Users.ProfileFieldVisibility.None);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
