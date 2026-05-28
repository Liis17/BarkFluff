using Amazon.S3.Model;

namespace BarkFluff.Files.Infrastructure;

public class S3Uploader : IS3Uploader
{
    private readonly IS3BucketRegistry _registry;

    public S3Uploader(IS3BucketRegistry registry)
    {
        _registry = registry;
    }

    public async Task<string> UploadAsync(string bucket, string key, Stream data, string contentType)
    {
        var client = _registry.GetClientForBucket(bucket);

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = data,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            ContentType = contentType,
            Metadata = { ["original-filename"] = Path.GetFileName(key) }
        };

        var response = await client.PutObjectAsync(request);

        return response.ETag;
    }

    /// <summary>
    /// Скачивает объект из S3 и отдаёт поток без буферизации в памяти.
    /// Возвращаемый поток владеет <see cref="GetObjectResponse"/>: при его Dispose/DisposeAsync
    /// корректно освобождаются HTTP-соединение и метаданные ответа AWS SDK.
    /// </summary>
    public async Task<Stream> DownloadAsync(string bucket, string key)
    {
        var client = _registry.GetClientForBucket(bucket);

        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = key
        };

        var response = await client.GetObjectAsync(request);

        return new S3ObjectStream(response);
    }

    /// <summary>
    /// Обёртка над <see cref="GetObjectResponse.ResponseStream"/>, которая при освобождении
    /// дополнительно диспозит сам ответ AWS SDK. Это нужно потому, что
    /// <see cref="GetObjectResponse"/> владеет HTTP-соединением и метаданными, и закрытие
    /// только потока не гарантирует возврат соединения в пул.
    /// </summary>
    private sealed class S3ObjectStream : Stream
    {
        private readonly GetObjectResponse _response;
        private readonly Stream _inner;

        public S3ObjectStream(GetObjectResponse response)
        {
            _response = response;
            _inner = response.ResponseStream;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _response.ContentLength > 0 ? _response.ContentLength : _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            _response.Dispose();
            await base.DisposeAsync();
        }
    }
}
