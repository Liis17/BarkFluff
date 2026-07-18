using Barkfluff.CloudMessaging.Consumers;
using Barkfluff.CloudMessaging.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;
using Grpc.Core;
using MassTransit;

namespace BarkFluff.CloudMessaging.Tests.Consumers;

public class AdminBroadcastConsumerTests
{
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient = new();
    private readonly Mock<FirebaseService> _firebaseService;
    private readonly ILogger<AdminBroadcastConsumer> _logger = Tests.TestHelper.CreateLogger<AdminBroadcastConsumer>();

    public AdminBroadcastConsumerTests()
    {
        _firebaseService = new Mock<FirebaseService>(
            Mock.Of<ILogger<FirebaseService>>(),
            Mock.Of<IConfiguration>());
    }

    private AdminBroadcastConsumer CreateConsumer()
    {
        return new AdminBroadcastConsumer(_usersClient.Object, _firebaseService.Object, _logger);
    }

    private static Mock<ConsumeContext<AdminBroadcastNotificationEvent>> CreateContext(
        AdminBroadcastNotificationEvent? @event = null)
    {
        var context = new Mock<ConsumeContext<AdminBroadcastNotificationEvent>>();
        context.Setup(c => c.Message).Returns(@event ?? new AdminBroadcastNotificationEvent());
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private void SetupGetAllDevices(params DeviceFirebaseToken[] tokens)
    {
        _usersClient
            .Setup(c => c.GetAllDevicesWithFirebaseTokensAsync(
                It.IsAny<GetAllDevicesWithFirebaseTokensRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Tests.TestHelper.CreateAsyncCall(new GetDevicesWithFirebaseTokensResponse
            {
                Tokens = { tokens }
            }));
    }

    private void SetupGetDevicesByDeviceIds(params DeviceFirebaseToken[] tokens)
    {
        _usersClient
            .Setup(c => c.GetDevicesWithFirebaseTokensByDeviceIdsAsync(
                It.IsAny<GetDevicesWithFirebaseTokensByDeviceIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Tests.TestHelper.CreateAsyncCall(new GetDevicesWithFirebaseTokensResponse
            {
                Tokens = { tokens }
            }));
    }

    [Fact]
    public async Task Consume_EmptyTitle_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent { Title = "", Body = "Body" };
        var context = CreateContext(@event);

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_NullTitle_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent { Title = null!, Body = "Body" };
        var context = CreateContext(@event);

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_EmptyBody_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent { Title = "Title", Body = "" };
        var context = CreateContext(@event);

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_NullBody_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent { Title = "Title", Body = null! };
        var context = CreateContext(@event);

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_WhitespaceOnlyTitle_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent { Title = "   ", Body = "Body" };
        var context = CreateContext(@event);

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_WhitespaceOnlyBody_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent { Title = "Title", Body = "   " };
        var context = CreateContext(@event);

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_NoTargetDeviceIds_FetchesAllDevices()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Announcement",
            Body = "Hello everyone!",
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        SetupGetAllDevices(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "token-1" },
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _usersClient.Verify(
            c => c.GetAllDevicesWithFirebaseTokensAsync(
                It.IsAny<GetAllDevicesWithFirebaseTokensRequest>(), null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new List<string> { "token-1", "token-2" })),
                "Announcement",
                "Hello everyone!",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_WithTargetDeviceIds_FetchesDevicesByIds()
    {
        var consumer = CreateConsumer();
        var deviceId1 = Guid.NewGuid();
        var deviceId2 = Guid.NewGuid();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Announcement",
            Body = "Hello!",
            TargetDeviceIds = [deviceId1, deviceId2]
        };
        var context = CreateContext(@event);

        SetupGetDevicesByDeviceIds(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "token-1" });

        await consumer.Consume(context.Object);

        _usersClient.Verify(
            c => c.GetDevicesWithFirebaseTokensByDeviceIdsAsync(
                It.Is<GetDevicesWithFirebaseTokensByDeviceIdsRequest>(r =>
                    r.DeviceIds.Contains(deviceId1.ToString()) &&
                    r.DeviceIds.Contains(deviceId2.ToString())),
                null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new List<string> { "token-1" })),
                "Announcement",
                "Hello!",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_NoDevicesWithTokens_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        SetupGetAllDevices();

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_EmptyTokensFiltered_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        SetupGetAllDevices(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "" },
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_MixedTokens_FiltersOnlyValid()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        SetupGetAllDevices(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "valid-token" },
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "" },
            new DeviceFirebaseToken { UserId = 3, DeviceId = "dev3", FirebaseToken = "" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.Count == 1 && t[0] == "valid-token"),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_WithImageUrl_PassesImageUrl()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            ImageUrl = "https://example.com/image.png",
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        SetupGetAllDevices(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                "Title",
                "Body",
                "https://example.com/image.png",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_NullImageUrl_PassesNull()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            ImageUrl = null,
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        SetupGetAllDevices(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                "Title",
                "Body",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_GrpcError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        _usersClient
            .Setup(c => c.GetAllDevicesWithFirebaseTokensAsync(
                It.IsAny<GetAllDevicesWithFirebaseTokensRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("gRPC error"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_FirebaseError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            TargetDeviceIds = []
        };
        var context = CreateContext(@event);

        SetupGetAllDevices(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "token" });

        _firebaseService
            .Setup(f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("FCM error"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_GetDevicesByDeviceIdsError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            TargetDeviceIds = [Guid.NewGuid()]
        };
        var context = CreateContext(@event);

        _usersClient
            .Setup(c => c.GetDevicesWithFirebaseTokensByDeviceIdsAsync(
                It.IsAny<GetDevicesWithFirebaseTokensByDeviceIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("gRPC error"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_PassesCancellationToken()
    {
        var consumer = CreateConsumer();
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var @event = new AdminBroadcastNotificationEvent
        {
            Title = "Title",
            Body = "Body",
            TargetDeviceIds = []
        };
        var context = new Mock<ConsumeContext<AdminBroadcastNotificationEvent>>();
        context.Setup(c => c.Message).Returns(@event);
        context.Setup(c => c.CancellationToken).Returns(token);

        SetupGetAllDevices(
            new DeviceFirebaseToken { UserId = 1, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendAdminBroadcastBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                token),
            Times.Once);
    }
}
