using BarkFluff.Files.Features.FetchFileStream;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Proto.Files;

using Grpc.Core;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Tests.Features.FetchFileStream;

/// <summary>
/// Стриминг содержимого файла ноде-партнёру (этап 3.2).
/// </summary>
public class FetchFileStreamQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private sealed class CollectingStreamWriter : IServerStreamWriter<FileStreamChunk>
    {
        public List<FileStreamChunk> Written { get; } = [];

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(FileStreamChunk message)
        {
            Written.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteAsync(FileStreamChunk message, CancellationToken cancellationToken)
        {
            Written.Add(message);
            return Task.CompletedTask;
        }
    }

    private FetchFileStreamQueryHandler CreateHandler()
        => new(
            _h.UploadedFilesStorage,
            _h.S3UploaderMock.Object,
            _h.S3BucketRegistryMock.Object,
            TestHelper.CreateLogger<FetchFileStreamQueryHandler>());

    private void SetupS3(byte[] content, long? totalSize = null, string? contentType = "image/jpeg")
    {
        _h.S3BucketRegistryMock
            .Setup(r => r.GetBucketName(It.IsAny<UploadFileType>()))
            .Returns("message-images");

        _h.S3UploaderMock
            .Setup(u => u.DownloadRangeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long?>()))
            .ReturnsAsync(() => new S3ObjectRange(
                new MemoryStream(content), totalSize ?? content.Length, contentType));
    }

    [Fact]
    public async Task Handle_SendsMetadataChunkFirst()
    {
        // Принимающая сторона по total_size контролирует объём и рвёт стрим при превышении (№44).
        var file = await _h.SeedFile(etag: "e1", filename: "cat.jpg", size: 3);
        SetupS3([1, 2, 3]);

        var writer = new CollectingStreamWriter();
        await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = file.Id }, writer, CancellationToken.None);

        var first = writer.Written.First();
        first.TotalSize.Should().Be(3);
        first.ContentType.Should().Be("image/jpeg");
        first.FileName.Should().Be("cat.jpg");
        first.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_StreamsAllBytes()
    {
        var content = Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray();
        var file = await _h.SeedFile(etag: "e1", size: content.Length);
        SetupS3(content);

        var writer = new CollectingStreamWriter();
        await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = file.Id }, writer, CancellationToken.None);

        var received = writer.Written.SelectMany(c => c.Data.ToByteArray()).ToArray();
        received.Should().Equal(content);
    }

    [Fact]
    public async Task Handle_LargeFile_IsSplitIntoChunks()
    {
        // Чанк держим заметно ниже лимита gRPC-сообщения (4 МБ).
        var content = new byte[FetchFileStreamQueryHandler.ChunkSize * 2 + 17];
        Random.Shared.NextBytes(content);
        var file = await _h.SeedFile(etag: "e1", size: content.Length);
        SetupS3(content);

        var writer = new CollectingStreamWriter();
        await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = file.Id }, writer, CancellationToken.None);

        // Метаданный чанк + три с данными.
        writer.Written.Should().HaveCount(4);
        writer.Written.Skip(1).Should()
            .OnlyContain(c => c.Data.Length <= FetchFileStreamQueryHandler.ChunkSize);
        writer.Written.SelectMany(c => c.Data.ToByteArray()).Should().Equal(content);
    }

    [Fact]
    public async Task Handle_RangeRequest_IsPassedToS3()
    {
        var file = await _h.SeedFile(etag: "e1", size: 1000);
        SetupS3([9, 9, 9], totalSize: 1000);

        var writer = new CollectingStreamWriter();
        await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = file.Id, RangeFrom = 100, RangeTo = 103 },
            writer, CancellationToken.None);

        _h.S3UploaderMock.Verify(u => u.DownloadRangeAsync(
            "message-images", file.Id.ToString(), 100, 103), Times.Once);

        // total_size — размер файла ЦЕЛИКОМ, а не длина выданного куска.
        writer.Written.First().TotalSize.Should().Be(1000);
    }

    [Fact]
    public async Task Handle_WholeFile_PassesNullUpperBound()
    {
        var file = await _h.SeedFile(etag: "e1", size: 3);
        SetupS3([1, 2, 3]);

        await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = file.Id },
            new CollectingStreamWriter(), CancellationToken.None);

        _h.S3UploaderMock.Verify(u => u.DownloadRangeAsync(
            It.IsAny<string>(), It.IsAny<string>(), 0, null), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownFile_ThrowsNotFound()
    {
        var act = async () => await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = Guid.NewGuid() },
            new CollectingStreamWriter(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task Handle_NotUploadedFile_ThrowsNotFound()
    {
        // «Нет файла» и «не загружен» наружу неразличимы.
        var file = await _h.SeedFile(etag: null);

        var act = async () => await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = file.Id },
            new CollectingStreamWriter(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task Handle_AttachmentType_IsNotFilteredByWhitelist()
    {
        // Публичный /download/{id} отдаёт только аватарки/обложки; здесь whitelist намеренно
        // не применяется — авторизацию уже сделал вызывающий (Federation).
        var file = await _h.SeedFile(etag: "e1", type: UploadFileType.MessageAttachmentDocument, size: 2);
        SetupS3([7, 7]);

        var writer = new CollectingStreamWriter();
        await CreateHandler().HandleAsync(
            new FetchFileStreamQuery { FileId = file.Id }, writer, CancellationToken.None);

        writer.Written.Should().HaveCountGreaterThan(1);
    }
}
