using BarkFluff.Files.Host;

namespace BarkFluff.Files.Tests.Host;

public class CountingReadStreamTests
{
    [Fact]
    public async Task ReadAsync_CountsBytesFromNonSeekableSource()
    {
        await using var stream = new CountingReadStream(new NonSeekableStream([1, 2, 3, 4, 5]));
        var buffer = new byte[3];

        (await stream.ReadAsync(buffer)).Should().Be(3);
        (await stream.ReadAsync(buffer)).Should().Be(2);

        stream.BytesRead.Should().Be(5);
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}
