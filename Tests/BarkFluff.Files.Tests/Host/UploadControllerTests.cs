using BarkFluff.Files.Domain;
using BarkFluff.Files.Features.UploadFile;
using BarkFluff.Files.Host;
using BarkFluff.Files.Persistence;

using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BarkFluff.Files.Tests.Host;

public class UploadControllerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly MetricsCollector _metrics = new();

    [Fact]
    public async Task UploadFile_CompletedOperation_ReturnsResultWithoutProcessingAgain()
    {
        var reservation = await ReserveAsync();
        var claim = await _helper.UploadedFilesStorage.ClaimUploadAsync(
            reservation.FileId, DateTime.UtcNow, TimeSpan.FromMinutes(30), CancellationToken.None);
        var resultFileId = Guid.NewGuid();
        await _helper.UploadedFilesStorage.CompleteUploadAsync(
            reservation.FileId, claim.LeaseToken!.Value, resultFileId, CancellationToken.None);

        var controller = CreateController();
        var result = await controller.UploadFile(reservation.FileId, CreateFile());

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(resultFileId, Assert.IsType<UploadFileResponse>(ok.Value).FileId);
        _mediator.Verify(
            mediator => mediator.Send(It.IsAny<UploadFileCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadFile_ProcessingOperation_ReturnsConflictAndRetryAfter()
    {
        var reservation = await ReserveAsync();
        await _helper.UploadedFilesStorage.ClaimUploadAsync(
            reservation.FileId, DateTime.UtcNow, TimeSpan.FromMinutes(30), CancellationToken.None);

        var controller = CreateController();
        var result = await controller.UploadFile(reservation.FileId, CreateFile());

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var status = Assert.IsType<UploadStatusResponse>(conflict.Value);
        Assert.Equal("processing", status.State);
        Assert.InRange(status.RetryAfterSeconds, 1, 5);
        Assert.Equal(status.RetryAfterSeconds.ToString(), controller.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task UploadFile_FailedProcessing_ReleasesLease()
    {
        var reservation = await ReserveAsync();
        _mediator
            .Setup(mediator => mediator.Send(It.IsAny<UploadFileCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateController().UploadFile(reservation.FileId, CreateFile()));

        var operation = await _helper.UploadedFilesStorage.GetUploadOperationAsync(reservation.FileId);
        Assert.Equal(UploadOperationState.Pending, operation!.State);
        Assert.Null(operation.LeaseToken);
    }

    [Fact]
    public async Task GetUploadStatus_CompletedOperation_ReturnsActualFileId()
    {
        var reservation = await ReserveAsync();
        var claim = await _helper.UploadedFilesStorage.ClaimUploadAsync(
            reservation.FileId, DateTime.UtcNow, TimeSpan.FromMinutes(30), CancellationToken.None);
        var resultFileId = Guid.NewGuid();
        await _helper.UploadedFilesStorage.CompleteUploadAsync(
            reservation.FileId, claim.LeaseToken!.Value, resultFileId, CancellationToken.None);

        var result = await CreateController().GetUploadStatus(reservation.FileId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<UploadStatusResponse>(ok.Value);
        Assert.Equal(resultFileId, status.FileId);
        Assert.Equal("completed", status.State);
        Assert.Equal(0, status.RetryAfterSeconds);
    }

    private Task<UploadReservation> ReserveAsync()
    {
        return _helper.UploadedFilesStorage.ReserveUploadAsync(
            42,
            Guid.NewGuid(),
            UploadFileType.MessageAttachmentDocument,
            DateTime.UtcNow,
            TimeSpan.FromHours(2),
            CancellationToken.None);
    }

    private UploadController CreateController()
    {
        return new UploadController(
            _mediator.Object,
            _helper.UploadedFilesStorage,
            _metrics)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static FormFile CreateFile()
    {
        return new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "test.bin");
    }
}
