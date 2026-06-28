using BarkFluff.Proto.Files;
using BarkFluff.Users.Features.Personalization.GetPersonalization;
using BarkFluff.Users.Features.Personalization.GetProfilePoster;
using BarkFluff.Users.Features.Personalization.GetProfilePosterServer;
using BarkFluff.Users.Features.Personalization.SetProfilePoster;
using BarkFluff.Users.Features.Personalization.SetProfilePosterServer;
using BarkFluff.Users.Features.Personalization.UpdatePersonalization;
using FluentAssertions;
using Grpc.Core;
using Moq;

namespace BarkFluff.Users.Tests.Features.Personalization;

public class GetProfilePosterServerQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_NoPersonalization_ReturnsEmptyUrl()
    {
        var user = await _h.SeedUser();
        var filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        var handler = new GetProfilePosterServerQueryHandler(_h.PersonalizationStorage, filesClient.Object, TestHelper.CreateLogger<GetProfilePosterServerQueryHandler>());

        var result = await handler.Handle(new GetProfilePosterServerQuery { UserId = user.Id }, CancellationToken.None);

        result.PosterUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithPoster_ReturnsFileUrl()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "poster-file-456", []);
        var filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        filesClient
            .Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetFileDataResponse>(
                Task.FromResult(new GetFileDataResponse { FileInfo = new UploadFileInfo { FileUrl = "https://files.com/poster.png" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => Metadata.Empty, () => { }));
        var handler = new GetProfilePosterServerQueryHandler(_h.PersonalizationStorage, filesClient.Object, TestHelper.CreateLogger<GetProfilePosterServerQueryHandler>());

        var result = await handler.Handle(new GetProfilePosterServerQuery { UserId = user.Id }, CancellationToken.None);

        result.PosterUrl.Should().Be("https://files.com/poster.png");
    }

    [Fact]
    public async Task Handle_GrpcError_ReturnsEmptyUrl()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "poster-file-err", []);
        var filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        filesClient
            .Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("gRPC error"));
        var handler = new GetProfilePosterServerQueryHandler(_h.PersonalizationStorage, filesClient.Object, TestHelper.CreateLogger<GetProfilePosterServerQueryHandler>());

        var result = await handler.Handle(new GetProfilePosterServerQuery { UserId = user.Id }, CancellationToken.None);

        result.PosterUrl.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
