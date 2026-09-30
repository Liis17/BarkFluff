using Barkfluff.CloudMessaging.Consumers;
using Barkfluff.CloudMessaging.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;
using Google.Protobuf;
using MassTransit;

namespace BarkFluff.CloudMessaging.Tests.Consumers;

public class EncryptedMessagePushConsumerTests
{
    private readonly Mock<UsersServerApi.UsersServerApiClient> _users = new();
    private readonly Mock<FirebaseService> _firebase = new(
        Mock.Of<ILogger<FirebaseService>>(), Mock.Of<IConfiguration>());

    [Fact]
    public async Task PrivateMessage_ExcludesSenderAndWeb_UsesChatMuteFilter()
    {
        var chatId = Guid.NewGuid();
        var message = new NewEncryptedMessageEvent
        {
            ChatId = chatId,
            ChatMembers = [1, 2, 2],
            Message = new EncryptedMessage { Id = 42, SenderId = 1, Ciphertext = ByteString.CopyFromUtf8("cipher") }.ToByteArray()
        };
        _users.Setup(client => client.GetDevicesWithFirebaseTokensAsync(
                It.Is<GetDevicesWithFirebaseTokensRequest>(request => request.ChatId == chatId.ToString() && request.UserIds.SequenceEqual(new[] { 2L })),
                null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new GetDevicesWithFirebaseTokensResponse
            {
                Tokens =
                {
                    new DeviceFirebaseToken { UserId = 1, FirebaseToken = "sender" },
                    new DeviceFirebaseToken { UserId = 2, FirebaseToken = "android" },
                    new DeviceFirebaseToken { UserId = 2, FirebaseToken = "android" },
                    new DeviceFirebaseToken { UserId = 2, FirebaseToken = "web", PushPlatform = PushPlatform.Web }
                }
            }));
        var consumer = new NewEncryptedMessagePushConsumer(_users.Object, _firebase.Object,
            Mock.Of<ILogger<NewEncryptedMessagePushConsumer>>());

        await consumer.Consume(Context(message));

        _firebase.Verify(service => service.SendPrivateMessageBatchAsync(
            It.Is<IReadOnlyList<string>>(tokens => tokens.SequenceEqual(new[] { "android" })),
            chatId.ToString(), 42, 1, It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyAll();
    }

    [Fact]
    public async Task SecretMessage_OnlyTargetsRecipientDevice()
    {
        var recipient = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var message = new NewSecretMessageEvent
        {
            MessageId = "event", SenderUserId = 1, SenderDeviceId = sender,
            RecipientUserId = 2, RecipientDeviceId = recipient, Envelope = [1, 2, 3]
        };
        _users.Setup(client => client.GetDevicesWithFirebaseTokensByDeviceIdsAsync(
                It.Is<GetDevicesWithFirebaseTokensByDeviceIdsRequest>(request => request.DeviceIds.SequenceEqual(new[] { recipient.ToString() })),
                null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new GetDevicesWithFirebaseTokensResponse
            {
                Tokens =
                {
                    new DeviceFirebaseToken { DeviceId = recipient.ToString(), FirebaseToken = "recipient" },
                    new DeviceFirebaseToken { DeviceId = Guid.NewGuid().ToString(), FirebaseToken = "other-device" },
                    new DeviceFirebaseToken { DeviceId = recipient.ToString(), FirebaseToken = "web", PushPlatform = PushPlatform.Web }
                }
            }));
        var consumer = new NewSecretMessagePushConsumer(_users.Object, _firebase.Object,
            Mock.Of<ILogger<NewSecretMessagePushConsumer>>());

        await consumer.Consume(Context(message));

        _firebase.Verify(service => service.SendSecretMessageBatchAsync(
            It.Is<IReadOnlyList<string>>(tokens => tokens.SequenceEqual(new[] { "recipient" })),
            "event", 1, sender.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyAll();
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.Setup(value => value.Message).Returns(message);
        return context.Object;
    }
}
