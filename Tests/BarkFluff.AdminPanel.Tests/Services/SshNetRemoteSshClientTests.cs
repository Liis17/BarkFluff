using Barkfluff.AdminPanel.Services;

using System.Text;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class SshNetRemoteSshClientTests
{
    [Fact]
    public async Task WriteToShellAsync_FlushesShortInputImmediately()
    {
        await using var stream = new FlushTrackingStream();
        var command = Encoding.UTF8.GetBytes("pwd\r");

        await SshNetRemoteSshClient.WriteToShellAsync(stream, command, 0, command.Length);

        Assert.True(stream.WasFlushed);
        Assert.Equal(command, stream.ToArray());
    }

    private sealed class FlushTrackingStream : MemoryStream
    {
        public bool WasFlushed { get; private set; }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            WasFlushed = true;
            return Task.CompletedTask;
        }
    }
}
