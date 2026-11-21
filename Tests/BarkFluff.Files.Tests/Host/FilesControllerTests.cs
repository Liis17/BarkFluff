using BarkFluff.Files.Features.DownloadFile;
using BarkFluff.Files.Host;
using BarkFluff.GrpcServer.Metrics;

using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace BarkFluff.Files.Tests.Host;

public class FilesControllerTests
{
    [Fact]
    public async Task DownloadFile_ExpiredTempLink_ReturnsNotFoundWithoutCountingError()
    {
        var helper = new TestHelper();
        var file = await helper.SeedFile(etag: "etag");
        var tempFile = await helper.SeedTempFile(file.Id, DateTime.UtcNow.AddMinutes(-1));
        var handler = new DownloadFileCommandHandler(
            helper.UploadedFilesStorage, helper.S3UploaderMock.Object, helper.S3BucketRegistryMock.Object,
            helper.TempFilesStorage, helper.BadgeImagesStorage,
            TestHelper.CreateLogger<DownloadFileCommandHandler>());
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<IRequest<DownloadFileResult>>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<DownloadFileResult> command, CancellationToken token) =>
                handler.Handle((DownloadFileCommand)command, token));
        var metrics = new MetricsCollector();
        var controller = new FilesController(mediator.Object, helper.TempFilesStorage,
            null!, null!, Mock.Of<IConfiguration>(), metrics, TestHelper.CreateLogger<FilesController>());

        var result = await controller.DownloadFile(tempFile.Id);

        result.Should().BeOfType<NotFoundObjectResult>();
        metrics.SnapshotAndReset().Should().NotContainKey("files_download_errors");
    }
}
