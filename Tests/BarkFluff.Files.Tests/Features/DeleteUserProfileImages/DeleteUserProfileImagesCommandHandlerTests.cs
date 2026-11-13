using Amazon.S3;
using Amazon.S3.Model;

using BarkFluff.Files.Domain;
using BarkFluff.Files.Features.DeleteUserProfileImages;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Tests.Features.DeleteUserProfileImages;

public class DeleteUserProfileImagesCommandHandlerTests
{
    [Fact]
    public async Task Handle_DeletesOwnedProfileFilesAndPreservesSharedFilesAndChatAttachments()
    {
        var helper = new TestHelper();
        const long userId = 17;
        const long otherUserId = 18;
        var mainId = Guid.NewGuid();
        var previewId = Guid.NewGuid();
        var sharedMainId = Guid.NewGuid();
        var sharedPreviewId = Guid.NewGuid();
        var posterId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        await helper.SeedFile(previewId, [userId], UploadFileType.UserAvatar, etag: "preview");
        await helper.SeedFile(mainId, [userId], UploadFileType.UserAvatar, etag: "main", previewId: previewId);
        await helper.SeedFile(sharedPreviewId, [userId], UploadFileType.UserAvatar, etag: "shared-preview");
        await helper.SeedFile(sharedMainId, [userId, otherUserId], UploadFileType.UserAvatar,
            etag: "shared-main", previewId: sharedPreviewId);
        await helper.SeedFile(posterId, [userId], UploadFileType.UserProfilePoster, etag: "poster");
        await helper.SeedFile(attachmentId, [userId], UploadFileType.MessageAttachmentImage, etag: "attachment");
        await helper.SeedTempFile(mainId);
        await helper.SeedTempFile(attachmentId);

        var s3 = new Mock<IAmazonS3>();
        s3.Setup(client => client.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());
        helper.S3BucketRegistryMock.Setup(registry => registry.GetBucketName(It.IsAny<UploadFileType>()))
            .Returns("profile-pictures");
        helper.S3BucketRegistryMock.Setup(registry => registry.GetClientForBucket("profile-pictures"))
            .Returns(s3.Object);
        var handler = new DeleteUserProfileImagesCommandHandler(helper.DbContext, helper.S3BucketRegistryMock.Object);

        await handler.Handle(new DeleteUserProfileImagesCommand { UserId = userId }, CancellationToken.None);
        await handler.Handle(new DeleteUserProfileImagesCommand { UserId = userId }, CancellationToken.None);

        (await helper.DbContext.UploadedFiles.Select(file => file.Id).ToListAsync())
            .Should().BeEquivalentTo(new[] { sharedMainId, sharedPreviewId, attachmentId });
        (await helper.DbContext.UploadedFiles.SingleAsync(file => file.Id == sharedMainId)).Uploaders
            .Should().ContainSingle().Which.Should().Be(otherUserId);
        (await helper.DbContext.TempFiles.Select(file => file.OriginalFileId).ToListAsync())
            .Should().BeEquivalentTo(new[] { attachmentId });
        s3.Verify(client => client.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }
}
