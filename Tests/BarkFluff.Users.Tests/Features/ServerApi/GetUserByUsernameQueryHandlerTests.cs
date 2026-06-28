using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Features.FindByLogin;
using BarkFluff.Users.Features.ListByIds;
using BarkFluff.Users.Features.GetUserByUsername;
using BarkFluff.Users.Features.SetProfilePictureServer;
using BarkFluff.Users.Features.UpdateProfileServer;
using BarkFluff.Users.Features.UpdateStorageLimit;
using FluentAssertions;
using Grpc.Core;
using Moq;

namespace BarkFluff.Users.Tests.Features.ServerApi;

public class GetUserByUsernameQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_PublicProfile_ReturnsFullInfo()
    {
        var user = await _h.SeedUser(username: "pubuser", bio: "My bio", profilePicture: "pic.png");
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: true);
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "pubuser" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.FirstName.Should().Be("Test");
        result.Bio.Should().Be("My bio");
        result.ProfilePicture.Should().Be("pic.png");
    }

    [Fact]
    public async Task Handle_HiddenProfile_ReturnsNotFound()
    {
        var user = await _h.SeedUser(username: "hiddenuser");
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: false);
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "hiddenuser" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DraftUser_ReturnsNotFound()
    {
        await _h.SeedUser(username: "draftuser", isDraft: true);
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "draftuser" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NonExistentUser_ReturnsNotFound()
    {
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "nobody" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_BioHidden_ReturnsEmptyBio()
    {
        var user = await _h.SeedUser(username: "biohidden", bio: "Secret bio");
        await _h.SeedPrivacy(user.Id, bioVisibility: Domain.ProfileFieldVisibility.None);
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "biohidden" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Bio.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AvatarHidden_ReturnsEmptyAvatar()
    {
        var user = await _h.SeedUser(username: "avatarhidden", profilePicture: "pic.png");
        await _h.SeedPrivacy(user.Id, avatarVisibility: Domain.ProfileFieldVisibility.None);
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "avatarhidden" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.ProfilePicture.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithPoster_ReturnsPosterUrl()
    {
        var user = await _h.SeedUser(username: "posteruser");
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: true);
        await _h.PersonalizationStorage.Update(user.Id, "poster-file-id", []);
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        filesClient
            .Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetFileDataResponse>(
                Task.FromResult(new GetFileDataResponse
                {
                    FileInfo = new UploadFileInfo { FileUrl = "https://files.com/poster.png" }
                }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => Metadata.Empty, () => { }));
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "posteruser" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.ProfilePosterUrl.Should().Be("https://files.com/poster.png");
    }

    [Fact]
    public async Task Handle_PosterGrpcError_ReturnsEmptyPoster()
    {
        var user = await _h.SeedUser(username: "postererr");
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: true);
        await _h.PersonalizationStorage.Update(user.Id, "poster-err-file", []);
        var filesClient = new Mock<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>();
        filesClient
            .Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("file service unavailable"));
        var handler = new GetUserByUsernameQueryHandler(
            _h.UsersStorage, _h.PrivacyStorage, _h.PersonalizationStorage, filesClient.Object, _h.Metrics);

        var result = await handler.Handle(new GetUserByUsernameQuery { Username = "postererr" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.ProfilePosterUrl.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
