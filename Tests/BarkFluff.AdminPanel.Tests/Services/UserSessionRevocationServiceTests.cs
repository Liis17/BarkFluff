using Barkfluff.AdminPanel.Services;

using BarkFluff.Proto.Identity;

using Grpc.Core;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

public class UserSessionRevocationServiceTests
{
    [Fact]
    public async Task RevokeAllAsync_WithNoSessions_ReturnsZeroCounts()
    {
        var invoker = new IdentityCallInvoker([]);
        var service = CreateService(invoker);

        var result = await service.RevokeAllAsync(42);

        Assert.Equal(0, result.RequestedCount);
        Assert.Equal(0, result.RevokedCount);
        Assert.Empty(result.FailedDeviceIds);
        Assert.Empty(invoker.RemovedDeviceIds);
    }

    [Fact]
    public async Task RevokeAllAsync_RevokesEveryActiveDevice()
    {
        var invoker = new IdentityCallInvoker(["phone", "desktop"]);
        var service = CreateService(invoker);

        var result = await service.RevokeAllAsync(42);

        Assert.Equal(2, result.RequestedCount);
        Assert.Equal(2, result.RevokedCount);
        Assert.Empty(result.FailedDeviceIds);
        Assert.Equal(new[] { "desktop", "phone" }, invoker.RemovedDeviceIds.Order());
    }

    [Fact]
    public async Task RevokeAllAsync_WhenOneDeviceFails_ReturnsPartialResult()
    {
        var invoker = new IdentityCallInvoker(["phone", "desktop"], failedDeviceId: "desktop");
        var service = CreateService(invoker);

        var result = await service.RevokeAllAsync(42);

        Assert.Equal(2, result.RequestedCount);
        Assert.Equal(1, result.RevokedCount);
        Assert.Equal(new[] { "desktop" }, result.FailedDeviceIds);
    }

    private static UserSessionRevocationService CreateService(IdentityCallInvoker invoker) =>
        new(new IdentityServerApi.IdentityServerApiClient(invoker));

    private sealed class IdentityCallInvoker(
        IReadOnlyCollection<string> activeDeviceIds,
        string? failedDeviceId = null) : CallInvoker
    {
        public List<string> RemovedDeviceIds { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            if (method.Name == "GetActiveSessionsServer")
            {
                var response = new GetActiveSessionsResponse();
                response.Sessions.AddRange(activeDeviceIds.Select(deviceId =>
                    new GetActiveSessionsResponse.Types.Session { DeviceId = deviceId }));
                return Call((TResponse)(object)response);
            }

            if (method.Name == "RemoveActiveSessionServer")
            {
                var remove = (RemoveActiveSessionServerRequest)(object)request!;
                RemovedDeviceIds.Add(remove.DeviceId);
                if (remove.DeviceId == failedDeviceId)
                    return FailedCall<TResponse>(new RpcException(new Status(StatusCode.Unavailable, "Identity unavailable")));

                return Call((TResponse)(object)new RemoveActiveSessionResponse());
            }

            throw new NotSupportedException(method.Name);
        }

        private static AsyncUnaryCall<T> Call<T>(T response) => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        private static AsyncUnaryCall<T> FailedCall<T>(Exception error) => new(
            Task.FromException<T>(error),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.Unavailable, error.Message),
            () => new Metadata(),
            () => { });

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();
    }
}
