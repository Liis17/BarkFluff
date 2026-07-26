using BarkFluff.Bots.Features.GetBotFile;
using BarkFluff.GrpcServer.Metrics;

using Grpc.Core;

using Moq;

using FilesProto = BarkFluff.Proto.Files;

using Xunit;

namespace BarkFluff.Bots.Tests.Features;

public class GetBotFileQueryHandlerTests
{
    private const string FileId = "8a1f4c3e-0d2b-4f6a-9c7e-1b3d5f7a9c11";

    private readonly Mock<FilesProto.FilesServerApi.FilesServerApiClient> _filesClient = new();
    private readonly GetBotFileQueryHandler _handler;

    public GetBotFileQueryHandlerTests()
    {
        _handler = new GetBotFileQueryHandler(_filesClient.Object, new MetricsCollector());
    }

    [Fact]
    public async Task Handle_KnownFile_ReturnsTempUrlWithMetadata()
    {
        SetupTempUrl(new FilesProto.GetTempDownloadUrlResponse
        {
            FileUrls =
            {
                new FilesProto.GetTempDownloadUrlResponse.Types.DownloadFileData
                {
                    FileId = FileId,
                    Url = "https://files.test/download/temp-token",
                },
            },
        });

        SetupFileData(new FilesProto.GetFileDataResponse
        {
            FileInfo = new FilesProto.UploadFileInfo { FileName = "report.pdf", FileSize = 2048 },
        });

        var result = await _handler.Handle(new GetBotFileQuery { FileId = FileId }, CancellationToken.None);

        Assert.Equal(FileId, result.FileId);
        Assert.Equal("https://files.test/download/temp-token", result.FileUrl);
        Assert.Equal("report.pdf", result.FileName);
        Assert.Equal(2048, result.FileSize);
    }

    [Fact]
    public async Task Handle_UnknownFile_ThrowsNotFound()
    {
        // Files не находит файл — возвращает пустой список ссылок, а не ошибку
        SetupTempUrl(new FilesProto.GetTempDownloadUrlResponse());

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _handler.Handle(new GetBotFileQuery { FileId = FileId }, CancellationToken.None));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Handle_NotAGuid_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _handler.Handle(new GetBotFileQuery { FileId = "not-a-guid" }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    private void SetupTempUrl(FilesProto.GetTempDownloadUrlResponse response)
        => _filesClient
            .Setup(c => c.GetTempDownloadUrlServerAsync(
                It.IsAny<FilesProto.GetTempDownloadUrlRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Call(response));

    private void SetupFileData(FilesProto.GetFileDataResponse response)
        => _filesClient
            .Setup(c => c.GetFileDataAsync(
                It.IsAny<FilesProto.GetFileDataRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Call(response));

    private static AsyncUnaryCall<T> Call<T>(T response)
        => new(Task.FromResult(response),
            Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
