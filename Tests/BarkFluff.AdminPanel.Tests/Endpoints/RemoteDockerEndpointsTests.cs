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
    public async Task ForwardShellOutputAsync_SendsPtyBytesAsBinaryFrames()
    {
        var socket = new CapturingWebSocket();
        var shell = new OutputShell(new byte[] { 0xD0 });

        await RemoteDockerEndpoints.ForwardShellOutputAsync(socket, shell, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Binary, socket.SentMessageType);
        Assert.Equal(new byte[] { 0xD0 }, socket.SentBytes);
    }

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

    private sealed class OutputShell(byte[] output) : IRemoteSshShell
    {
        private bool _read;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            if (_read)
                return Task.FromResult(0);

            _read = true;
            Array.Copy(output, 0, buffer, offset, output.Length);
            return Task.FromResult(output.Length);
        }

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingWebSocket : WebSocket
    {
        public WebSocketMessageType? SentMessageType { get; private set; }
        public byte[] SentBytes { get; private set; } = [];
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            SentMessageType = messageType;
            SentBytes = buffer.ToArray();
            return Task.CompletedTask;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override void Abort() { }

        public override void Dispose() { }
    }
}
