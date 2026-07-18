using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Services;
using BarkFluff.Proto.Federation;

using FluentAssertions;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace BarkFluff.Federation.Tests.Services;

public class EventSignerTests
{
    private static (FederationSigningKey key, byte[] publicKey) GenerateKey(string keyId)
    {
        var random = new SecureRandom();
        var priv = new Ed25519PrivateKeyParameters(random);
        var pub = priv.GeneratePublicKey();

        var entity = new FederationSigningKey
        {
            KeyId = keyId,
            PublicKey = pub.GetEncoded(),
            PrivateKeySeed = priv.GetEncoded(),
            CreatedAt = DateTime.UtcNow,
        };
        return (entity, pub.GetEncoded());
    }

    private static FederationEvent BuildSampleEvent(Guid? eventId = null)
    {
        var evt = new FederationEvent
        {
            EventId = (eventId ?? Guid.NewGuid()).ToString(),
            OriginServer = "node-a.test",
            OriginTsMs = 1789000000000L,
        };
        evt.NewMessage = new NewMessagePayload
        {
            ChatId = Guid.NewGuid().ToString(),
            FederatedMessageId = Guid.NewGuid().ToString(),
            Sender = new FederatedUser { Uuid = Guid.NewGuid().ToString(), Username = "bob", ServerName = "node-a.test" },
            Text = "hi",
        };
        return evt;
    }

    [Fact]
    public void Sign_Then_Verify_Succeeds()
    {
        var (key, _) = GenerateKey("ed25519:1");
        var evt = BuildSampleEvent();

        EventSigner.Sign(evt, key);
        evt.OriginKeyId.Should().Be("ed25519:1");
        evt.OriginSignature.Should().NotBeEmpty();

        EventSigner.Verify(evt, key.PublicKey).Should().BeTrue();
    }

    [Fact]
    public void Verify_Fails_WhenPayloadTampered()
    {
        var (key, _) = GenerateKey("ed25519:1");
        var evt = BuildSampleEvent();
        EventSigner.Sign(evt, key);

        // Меняем текст сообщения — wire-байты после очистки подписи разойдутся.
        evt.NewMessage.Text = "tampered";

        EventSigner.Verify(evt, key.PublicKey).Should().BeFalse();
    }

    [Fact]
    public void Verify_Fails_With_ForeignKey()
    {
        var (originKey, _) = GenerateKey("ed25519:1");
        var (_, foreignPub) = GenerateKey("ed25519:2");

        var evt = BuildSampleEvent();
        EventSigner.Sign(evt, originKey);

        EventSigner.Verify(evt, foreignPub).Should().BeFalse();
    }

    [Fact]
    public void Verify_Fails_WhenSignatureMissing()
    {
        var (_, pub) = GenerateKey("ed25519:1");
        var evt = BuildSampleEvent();

        // Подпись не проставлена.
        EventSigner.Verify(evt, pub).Should().BeFalse();
    }

    [Fact]
    public void Sign_IsDeterministic_ForSameEventAndKey()
    {
        var (key, _) = GenerateKey("ed25519:1");
        var eventId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var senderUuid = Guid.NewGuid();
        var msgId = Guid.NewGuid();

        var a = new FederationEvent
        {
            EventId = eventId.ToString(),
            OriginServer = "node-a.test",
            OriginTsMs = 1789000000000L,
        };
        a.NewMessage = new NewMessagePayload
        {
            ChatId = chatId.ToString(),
            FederatedMessageId = msgId.ToString(),
            Sender = new FederatedUser { Uuid = senderUuid.ToString(), Username = "bob", ServerName = "node-a.test" },
            Text = "hi",
        };

        var b = new FederationEvent
        {
            EventId = eventId.ToString(),
            OriginServer = "node-a.test",
            OriginTsMs = 1789000000000L,
        };
        b.NewMessage = new NewMessagePayload
        {
            ChatId = chatId.ToString(),
            FederatedMessageId = msgId.ToString(),
            Sender = new FederatedUser { Uuid = senderUuid.ToString(), Username = "bob", ServerName = "node-a.test" },
            Text = "hi",
        };

        EventSigner.Sign(a, key);
        EventSigner.Sign(b, key);

        a.OriginSignature.Should().Equal(b.OriginSignature);
        a.OriginKeyId.Should().Be(b.OriginKeyId);
    }
}
