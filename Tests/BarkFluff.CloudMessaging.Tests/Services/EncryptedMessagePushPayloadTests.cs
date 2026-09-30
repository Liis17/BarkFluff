using Barkfluff.CloudMessaging.Services;

namespace BarkFluff.CloudMessaging.Tests.Services;

public class EncryptedMessagePushPayloadTests
{
    [Fact]
    public async Task PrivatePayload_OnlyContainsMetadata_AndCannotEnterLegacyMessageParser()
    {
        var service = new RecordingFirebaseService();
        await service.SendPrivateMessageBatchAsync(["token"], "private-chat", 42, 7);
        service.Data.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["type"] = "new_private_message", ["private_chat_id"] = "private-chat",
            ["event_id"] = "42", ["sender_user_id"] = "7"
        });
        service.Data.Should().NotContainKey("chat_id");
    }

    [Fact]
    public async Task SecretPayload_OnlyContainsMetadata_AndCannotEnterLegacyMessageParser()
    {
        var service = new RecordingFirebaseService();
        await service.SendSecretMessageBatchAsync(["token"], "event", 7, "peer-device");
        service.Data.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["type"] = "new_secret_message", ["event_id"] = "event",
            ["sender_user_id"] = "7", ["sender_device_id"] = "peer-device"
        });
        service.Data.Should().NotContainKey("chat_id");
    }

    private sealed class RecordingFirebaseService() : FirebaseService(
        Mock.Of<ILogger<FirebaseService>>(), Mock.Of<IConfiguration>())
    {
        public Dictionary<string, string> Data { get; private set; } = new();

        protected override Task SendEncryptedDataBatchAsync(IReadOnlyList<string> tokens,
            Dictionary<string, string> data, CancellationToken cancellationToken)
        {
            Data = data;
            return Task.CompletedTask;
        }
    }
}
