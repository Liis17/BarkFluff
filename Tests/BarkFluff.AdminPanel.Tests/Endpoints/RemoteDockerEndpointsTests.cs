using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

using System.Net.WebSockets;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Endpoints;

public class RemoteDockerEndpointsTests
{
    [Fact]
    public async Task HandleConsoleAsync_DoesNotOpenSshWhenWebSocketUpgradeFails()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new FailingWebSocketFeature());
        var shellOpened = false;

        await RemoteDockerEndpoints.HandleConsoleAsync(
            context,
            Guid.Empty,
            _ =>
            {
                shellOpened = true;
                return Task.FromResult<IRemoteSshShell>(new FakeShell());
            },
            NullLogger<RemoteDockerService>.Instance,
            CancellationToken.None);

        Assert.False(shellOpened);
        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
    }

    private sealed class FailingWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            Task.FromException<WebSocket>(new InvalidOperationException("upgrade failed"));
    }

    private sealed class FakeShell : IRemoteSshShell
    {
        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
