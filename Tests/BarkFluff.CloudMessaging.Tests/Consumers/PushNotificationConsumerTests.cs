using Barkfluff.CloudMessaging.Consumers;
using Barkfluff.CloudMessaging.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;
using Grpc.Core;
using MassTransit;

namespace BarkFluff.CloudMessaging.Tests.Consumers;

public class PushNotificationConsumerTests
{
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient = new();
    private readonly Mock<MessagesApi.MessagesApiClient> _messagesClient = new();
    private readonly Mock<FirebaseService> _firebaseService;
    private readonly ILogger<PushNotificationConsumer> _logger = Tests.TestHelper.CreateLogger<PushNotificationConsumer>();

    public PushNotificationConsumerTests()
    {
        _firebaseService = new Mock<FirebaseService>(
            Mock.Of<ILogger<FirebaseService>>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IDismissPushSender>());
    }

    private PushNotificationConsumer CreateConsumer()
    {
        return new PushNotificationConsumer(
            _usersClient.Object,
            _messagesClient.Object,
            _firebaseService.Object,
            _logger);
    }

    private static Mock<ConsumeContext<PushNotificationEvent>> CreateContext(
        PushNotificationEvent? @event = null)
    {
        var context = new Mock<ConsumeContext<PushNotificationEvent>>();
        context.Setup(c => c.Message).Returns(@event ?? new PushNotificationEvent());
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private void SetupGetById(User? user)
    {
        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Tests.TestHelper.CreateAsyncCall(new GetByIdResponse { User = user }));
    }

    private void SetupGetChatInfo(GetChatInfoResponse response)
    {
        _messagesClient
            .Setup(c => c.GetChatInfoAsync(It.IsAny<GetChatInfoRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Tests.TestHelper.CreateAsyncCall(response));
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
    public async Task Consume_NoRecipients_DoesNotCallGrpcOrFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 100,
            RecipientUserIds = []
        };
        var context = CreateContext(@event);

        await consumer.Consume(context.Object);

        _usersClient.Verify(
            c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()),
            Times.Never);
        _messagesClient.Verify(
            c => c.GetChatInfoAsync(It.IsAny<GetChatInfoRequest>(), null, null, It.IsAny<CancellationToken>()),
            Times.Never);
        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_WithRecipients_FetchesSenderAndChatInfoInParallel()
    {
        var consumer = CreateConsumer();
        var senderId = 42L;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = senderId,
            MessageId = 100,
            RecipientUserIds = [2, 3],
            MessageText = "Hello"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = senderId, FirstName = "Ivan", LastName = "Petrov", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false, Title = "DM Chat" });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" },
            new DeviceFirebaseToken { UserId = 3, FirebaseToken = "token-3" });

        await consumer.Consume(context.Object);

        _usersClient.Verify(
            c => c.GetByIdAsync(
                It.Is<GetByIdRequest>(r => r.UserId == senderId),
                null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _messagesClient.Verify(
            c => c.GetChatInfoAsync(
                It.Is<GetChatInfoRequest>(r => r.ChatId == @event.ChatId.ToString()),
                null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_SenderHasFirstAndLastName_UsesFullName()
    {
        var consumer = CreateConsumer();
        var senderId = 1L;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = senderId,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = senderId, FirstName = "Ivan", LastName = "Petrov", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.Is<IReadOnlyList<string>>(tokens => tokens.SequenceEqual(new List<string> { "token-2" })),
                "Ivan Petrov",
                "Hi",
                @event.ChatId.ToString(),
                senderId,
                1,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                false,
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_SenderHasOnlyFirstName_UsesFirstName()
    {
        var consumer = CreateConsumer();
        var senderId = 1L;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = senderId,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = senderId, FirstName = "Ivan", LastName = "", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                "Ivan",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_SenderHasNoName_UsesUsername()
    {
        var consumer = CreateConsumer();
        var senderId = 1L;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = senderId,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = senderId, FirstName = "", LastName = "", Username = "cooluser" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                "cooluser",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_SenderNullUser_UsesUnknown()
    {
        var consumer = CreateConsumer();
        var senderId = 1L;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = senderId,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(null);
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                "Unknown",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_SenderProfilePicturePreview_PassedAsAvatar()
    {
        var consumer = CreateConsumer();
        var senderId = 1L;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = senderId,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = senderId, FirstName = "Ivan", LastName = "P", Username = "ivan", ProfilePicturePreview = "https://avatar.url/pic.jpg" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                "https://avatar.url/pic.jpg",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_GroupChat_PassesGroupChatInfo()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2, 3],
            MessageText = "Hello group"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = true, Title = "Dev Chat", Picture = "https://chat.avatar/pic.jpg" });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" },
            new DeviceFirebaseToken { UserId = 3, FirebaseToken = "token-3" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.Count == 2),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                "Dev Chat",
                "https://chat.avatar/pic.jpg",
                true,
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_NoDevicesWithTokens_DoesNotCallFirebase()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens();

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_DevicesWithEmptyTokens_FiltersOutEmptyTokens()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "valid-token" },
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "" },
            new DeviceFirebaseToken { UserId = 2, DeviceId = "dev2", FirebaseToken = "" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.Count == 1 && t[0] == "valid-token"),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Consumer не делает ранний выход при пустых после фильтрации токенах:
    // он вызывает Firebase с пустым списком (guard на Count == 0 живёт уже в FirebaseService).
    [Fact]
    public async Task Consume_AllTokensEmpty_CallsFirebaseWithEmptyTokenList()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "" },
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.Count == 0),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_NullMessageText_PassesEmptyString()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = null
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                "",
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_ContentTypeAndAttachments_PassedCorrectly()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Photo",
            ContentType = 2,
            ImagePreviewUrl = "https://img.preview/thumb.jpg",
            AttachmentCount = 3
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                2,
                "https://img.preview/thumb.jpg",
                3,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_GrpcError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("gRPC connection failed"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_FirebaseError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token" });

        _firebaseService
            .Setup(f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("FCM error"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_ChatInfoEmptyTitle_PassesEmptyString()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false, Title = "" });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                "",
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_PassesCancellationTokenToFirebase()
    {
        var consumer = CreateConsumer();
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = new Mock<ConsumeContext<PushNotificationEvent>>();
        context.Setup(c => c.Message).Returns(@event);
        context.Setup(c => c.CancellationToken).Returns(token);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                token),
            Times.Once);
    }

    [Fact]
    public async Task Consume_MultipleRecipients_SendsToAllTokens()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2, 3, 4],
            MessageText = "Hello all"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = true, Title = "Group" });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" },
            new DeviceFirebaseToken { UserId = 3, FirebaseToken = "token-3" },
            new DeviceFirebaseToken { UserId = 4, FirebaseToken = "token-4" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new List<string> { "token-2", "token-3", "token-4" })),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_GetDevicesError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });

        _usersClient
            .Setup(c => c.GetDevicesWithFirebaseTokensAsync(
                It.IsAny<GetDevicesWithFirebaseTokensRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("Devices service down"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_GetChatInfoError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = 1, FirstName = "Ivan", LastName = "P", Username = "ivan" });

        _messagesClient
            .Setup(c => c.GetChatInfoAsync(It.IsAny<GetChatInfoRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("ChatInfo gRPC failed"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_ParallelFetchError_DoesNotThrow()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("Sender fetch failed"));
        _messagesClient
            .Setup(c => c.GetChatInfoAsync(It.IsAny<GetChatInfoRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new Exception("ChatInfo fetch failed"));

        var act = async () => await consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_SenderEmptyUsername_FallsBackToEmptyString()
    {
        var consumer = CreateConsumer();
        var senderId = 1L;
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = senderId,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(new User { Id = senderId, FirstName = "", LastName = "", Username = "" });
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token-2" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                "",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_SenderNullUser_NullAvatar_PassesEmptyString()
    {
        var consumer = CreateConsumer();
        var @event = new PushNotificationEvent
        {
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            MessageId = 1,
            RecipientUserIds = [2],
            MessageText = "Hi"
        };
        var context = CreateContext(@event);

        SetupGetById(null);
        SetupGetChatInfo(new GetChatInfoResponse { IsGroupChat = false });
        SetupGetDevicesWithTokens(
            new DeviceFirebaseToken { UserId = 2, FirebaseToken = "token" });

        await consumer.Consume(context.Object);

        _firebaseService.Verify(
            f => f.SendNotificationBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                "",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
