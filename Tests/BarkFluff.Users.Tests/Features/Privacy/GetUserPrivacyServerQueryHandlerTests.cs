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
        var handler = new GetUserPrivacyServerQueryHandler(_h.PrivacyStorage);

        var result = await handler.Handle(new GetUserPrivacyServerQuery { UserId = user.Id }, CancellationToken.None);

        result.Settings.SearchVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoPrivacy_CreatesDefault()
    {
        var user = await _h.SeedUser();
        var handler = new GetUserPrivacyServerQueryHandler(_h.PrivacyStorage);

        var result = await handler.Handle(new GetUserPrivacyServerQuery { UserId = user.Id }, CancellationToken.None);

        result.Settings.Should().NotBeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
