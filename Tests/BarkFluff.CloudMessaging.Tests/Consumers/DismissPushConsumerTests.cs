using Barkfluff.CloudMessaging.Consumers;
using Barkfluff.CloudMessaging.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;
using Grpc.Core;
using MassTransit;

namespace BarkFluff.CloudMessaging.Tests.Consumers;

public class DismissPushConsumerTests
{
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient = new();
    private readonly Mock<FirebaseService> _firebaseService;
    private readonly ILogger<DismissPushConsumer> _logger = Tests.TestHelper.CreateLogger<DismissPushConsumer>();

    public DismissPushConsumerTests()
    {
        _firebaseService = new Mock<FirebaseService>(
            Mock.Of<ILogger<FirebaseService>>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IDismissPushSender>());
    }

    private DismissPushConsumer CreateConsumer()
    {
        return new DismissPushConsumer(_usersClient.Object, _firebaseService.Object, _logger);
    }

    private static Mock<ConsumeContext<DismissPushEvent>> CreateContext(DismissPushEvent? @event = null)
    {
        var context = new Mock<ConsumeContext<DismissPushEvent>>();
        context.Setup(c => c.Message).Returns(@event ?? new DismissPushEvent());
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private void SetupGetDevicesWithTokens(params DeviceFirebaseToken[] tokens)
    {
        _usersClient
            .Setup(c => c.GetDevicesWithFirebaseTokensAsync(
                It.IsAny<GetDevicesWithFirebaseTokensRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Tests.TestHelper.CreateAsyncCall(new GetDevicesWithFirebaseTokensResponse
            {
                Tokens = { tokens }
            }));
    }

    [Fact]
    public async Task Consume_WithTokens_SendsDismiss()
    {
        var consumer = CreateConsumer();
        var chatId = Guid.NewGuid();
        var @event = new DismissPushEvent { ChatId = chatId, UserId = 42 };
        var context = CreateContext(@event);

        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "token-1" },
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendDismissBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new List<string> { "token-1", "token-2" })),
                chatId.ToString(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_DuplicateTokens_SendsEachTokenOnce()
    {
        var consumer = CreateConsumer();
        var chatId = Guid.NewGuid();
        var context = CreateContext(new DismissPushEvent { ChatId = chatId, UserId = 42 });

        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 42, DeviceId = "device-1", FirebaseToken = "same-token" },
            new DeviceFirebaseToken { UserId = 42, DeviceId = "device-2", FirebaseToken = "same-token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendDismissBatchAsync(
                It.Is<IReadOnlyList<string>>(tokens => tokens.SequenceEqual(new[] { "same-token" })),
                chatId.ToString(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_NoDevices_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new DismissPushEvent { ChatId = Guid.NewGuid(), UserId = 42 };
        var context = CreateContext(@event);

        SetupGetDevicesWithTokens();

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendDismissBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_DevicesWithEmptyTokens_FiltersOutEmptyTokens()
    {
        var consumer = CreateConsumer();
        var @event = new DismissPushEvent { ChatId = Guid.NewGuid(), UserId = 42 };
        var context = CreateContext(@event);

        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "valid-token" },
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "" },
            new DeviceFirebaseToken { UserId = 42, DeviceId = "dev3", FirebaseToken = "" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendDismissBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.Count == 1 && t[0] == "valid-token"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_AllTokensEmpty_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new DismissPushEvent { ChatId = Guid.NewGuid(), UserId = 42 };
        var context = CreateContext(@event);

        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "" },
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendDismissBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_GrpcError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new DismissPushEvent { ChatId = Guid.NewGuid(), UserId = 42 };
        var context = CreateContext(@event);

        _usersClient
            .Setup(c => c.GetDevicesWithFirebaseTokensAsync(
                It.IsAny<GetDevicesWithFirebaseTokensRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("gRPC connection failed"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_FirebaseError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new DismissPushEvent { ChatId = Guid.NewGuid(), UserId = 42 };
        var context = CreateContext(@event);

        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "token" });

        _firebaseService
            .Setup(f => f.SendDismissBatchAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("FCM error"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_PassesCancellationToken()
    {
        var consumer = CreateConsumer();
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var chatId = Guid.NewGuid();
        var @event = new DismissPushEvent { ChatId = chatId, UserId = 42 };
        var context = new Mock<ConsumeContext<DismissPushEvent>>();
        context.Setup(c => c.Message).Returns(@event);
        context.Setup(c => c.CancellationToken).Returns(token);

        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendDismissBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                chatId.ToString(),
                token),
            Times.Once);
    }

    [Fact]
    public async Task Consume_RequestsTokensForCorrectUserId()
    {
        var consumer = CreateConsumer();
        var userId = 42L;
        var @event = new DismissPushEvent { ChatId = Guid.NewGuid(), UserId = userId };
        var context = CreateContext(@event);

        SetupGetDevicesWithTokens();

        await consumer.Consume(context.Object);

        _usersClient.Verify(
            c => c.GetDevicesWithFirebaseTokensAsync(
                It.Is<GetDevicesWithFirebaseTokensRequest>(r => r.UserIds.Contains(userId)),
                null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_PassesCorrectChatId()
    {
        var consumer = CreateConsumer();
        var chatId = Guid.NewGuid();
        var @event = new DismissPushEvent { ChatId = chatId, UserId = 42 };
        var context = CreateContext(@event);

        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 42, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendDismissBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                chatId.ToString(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
