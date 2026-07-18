using BarkFluff.Users.Features.GetFederatedProfile;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.ServerApi;

public class GetFederatedProfileQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ByUsername_PublicProfile_ReturnsFullInfoWithUuid()
    {
        var user = await _h.SeedUser(username: "pubfed", bio: "My bio", profilePicture: "file-id-1");
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: true);
        var handler = new GetFederatedProfileQueryHandler(_h.UsersStorage, _h.PrivacyStorage, _h.Metrics);

        var result = await handler.Handle(new GetFederatedProfileQuery { Username = "pubfed" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Uuid.Should().Be(user.Uuid.ToString());
        result.Username.Should().Be("pubfed");
        result.Bio.Should().Be("My bio");
        result.AvatarFileId.Should().Be("file-id-1");
    }

    [Fact]
    public async Task Handle_ByUuid_ReturnsProfile()
    {
        var user = await _h.SeedUser(username: "uuiduser", firstName: "UU");
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: true);
        var handler = new GetFederatedProfileQueryHandler(_h.UsersStorage, _h.PrivacyStorage, _h.Metrics);

        var result = await handler.Handle(new GetFederatedProfileQuery { Uuid = user.Uuid }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Uuid.Should().Be(user.Uuid.ToString());
        result.Username.Should().Be("uuiduser");
        result.FirstName.Should().Be("UU");
    }

    [Fact]
    public async Task Handle_HiddenProfile_ReturnsNotFound()
    {
        var user = await _h.SeedUser(username: "hidden");
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: false);
        var handler = new GetFederatedProfileQueryHandler(_h.UsersStorage, _h.PrivacyStorage, _h.Metrics);

        var result = await handler.Handle(new GetFederatedProfileQuery { Username = "hidden" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DraftUser_ReturnsNotFound()
    {
        await _h.SeedUser(username: "draft", isDraft: true);
        var handler = new GetFederatedProfileQueryHandler(_h.UsersStorage, _h.PrivacyStorage, _h.Metrics);

        var result = await handler.Handle(new GetFederatedProfileQuery { Username = "draft" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_BioHidden_ReturnsEmptyBio()
    {
        var user = await _h.SeedUser(username: "biohidden", bio: "Secret");
        await _h.SeedPrivacy(user.Id, bioVisibility: Domain.ProfileFieldVisibility.None);
        var handler = new GetFederatedProfileQueryHandler(_h.UsersStorage, _h.PrivacyStorage, _h.Metrics);

        var result = await handler.Handle(new GetFederatedProfileQuery { Username = "biohidden" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Bio.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AvatarHidden_ReturnsEmptyAvatar()
    {
        var user = await _h.SeedUser(username: "avatarhidden", profilePicture: "pic.png");
        await _h.SeedPrivacy(user.Id, avatarVisibility: Domain.ProfileFieldVisibility.None);
        var handler = new GetFederatedProfileQueryHandler(_h.UsersStorage, _h.PrivacyStorage, _h.Metrics);

        var result = await handler.Handle(new GetFederatedProfileQuery { Username = "avatarhidden" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.AvatarFileId.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NonExistentUser_ReturnsNotFound()
    {
        var handler = new GetFederatedProfileQueryHandler(_h.UsersStorage, _h.PrivacyStorage, _h.Metrics);

        var result = await handler.Handle(new GetFederatedProfileQuery { Username = "nobody" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
