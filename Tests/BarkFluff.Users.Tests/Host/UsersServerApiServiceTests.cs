using BarkFluff.Proto.Users;
using BarkFluff.Users.Host;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Users.Tests.Host;

public class UsersServerApiServiceTests
{
    [Fact]
    public async Task UpdateDeviceAppInfo_InvalidDeviceId_ReturnsInvalidArgument()
    {
        var mediator = new Mock<IMediator>();
        var service = new UsersServerApiService(mediator.Object, new MetricsCollector());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.UpdateDeviceAppInfo(
            new UpdateDeviceAppInfoRequest { DeviceId = "Android" }, null!));

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        mediator.Verify(m => m.Send(It.IsAny<IRequest<UpdateDeviceAppInfoResponse>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
