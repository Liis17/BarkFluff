using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.ChangeBio;
using BarkFluff.Users.Features.ChangeName;
using BarkFluff.Users.Features.ChangeUsername;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.GetUserContacts;
using BarkFluff.Users.Features.SetProfilePicture;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BarkFluff.Users.Tests.Features.Profile;

public class SetProfilePictureCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_WithFileId_SetsProfilePicture()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        filesClient
            .Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetFileDataResponse>(
                Task.FromResult(new GetFileDataResponse
                {
                    FileInfo = new UploadFileInfo
                    {
                        Type = UploadFileType.UserAvatar,
                        FileUrl = "https://files.com/avatar.png",
                        PreviewUrl = "https://files.com/avatar_small.png"
                    }
                }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => Metadata.Empty,
                () => { }));

        var handler = new SetProfilePictureCommandHandler(
            filesClient.Object, _h.UsersStorage, ctx, _h.QueueSender, _h.Metrics,
            TestHelper.CreateLogger<SetProfilePictureCommandHandler>());

        var result = await handler.Handle(new SetProfilePictureCommand { FileId = Guid.NewGuid() }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.ProfilePicture.Should().Be("https://files.com/avatar.png");
        updated.ProfilePicturePreviewUrl.Should().Be("https://files.com/avatar_small.png");
    }

    [Fact]
    public async Task Handle_InvalidFileType_ThrowsProfilePictureHasNotValidType()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        filesClient
            .Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetFileDataResponse>(
                Task.FromResult(new GetFileDataResponse
                {
                    FileInfo = new UploadFileInfo { Type = UploadFileType.ChatPicture }
                }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => Metadata.Empty,
                () => { }));

        var handler = new SetProfilePictureCommandHandler(
            filesClient.Object, _h.UsersStorage, ctx, _h.QueueSender, _h.Metrics,
            TestHelper.CreateLogger<SetProfilePictureCommandHandler>());

        var act = () => handler.Handle(new SetProfilePictureCommand { FileId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<ProfilePictureHasNotValidType>();
    }

    [Fact]
    public async Task Handle_NullFileId_RemovesProfilePicture()
    {
        var user = await _h.SeedUser(profilePicture: "https://old.com/pic.png");
        var ctx = _h.CreateUserContext(user.Id);
        var filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        var handler = new SetProfilePictureCommandHandler(
            filesClient.Object, _h.UsersStorage, ctx, _h.QueueSender, _h.Metrics,
            TestHelper.CreateLogger<SetProfilePictureCommandHandler>());

        await handler.Handle(new SetProfilePictureCommand { FileId = null }, CancellationToken.None);

        var updated = await _h.UsersStorage.GetById(user.Id);
        updated!.ProfilePicture.Should().BeEmpty();
        updated.ProfilePicturePreviewUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_GrpcError_ThrowsRpcException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        filesClient
            .Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, CancellationToken.None))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "file not found")));
        var handler = new SetProfilePictureCommandHandler(
            filesClient.Object, _h.UsersStorage, ctx, _h.QueueSender, _h.Metrics,
            TestHelper.CreateLogger<SetProfilePictureCommandHandler>());

        var act = () => handler.Handle(new SetProfilePictureCommand { FileId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<RpcException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
