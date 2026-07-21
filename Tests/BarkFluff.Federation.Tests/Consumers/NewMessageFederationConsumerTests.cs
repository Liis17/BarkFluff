using BarkFluff.Federation.Consumers;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Federation;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Moq;

namespace BarkFluff.Federation.Tests.Consumers;

public class NewMessageFederationConsumerTests
{
    private static (FederationContext Context, NewMessageFederationConsumer Consumer) Create(IConfiguration? configuration = null)
    {
        var context = TestHelpers.CreateContext();
        var config = configuration ?? TestHelpers.CreateConfiguration();
        var writer = new OutboxWriter(context, TestHelpers.CreateSigningKeyService(context), config, new MetricsCollector());
        return (context, new NewMessageFederationConsumer(writer, config, new MetricsCollector()));
    }

    private static ConsumeContext<NewMessageEvent> ConsumeContextOf(NewMessageEvent message)
    {
        var context = new Mock<ConsumeContext<NewMessageEvent>>();
        context.Setup(c => c.Message).Returns(message);
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static NewMessageEvent FederatedMessage(params string[] remoteServers)
        => new()
        {
            ChatId = Guid.NewGuid(),
            IsFederated = true,
            FederatedId = Guid.NewGuid(),
            SenderUuid = Guid.NewGuid(),
            LastChangeAt = DateTimeOffset.UtcNow,
            RemoteParticipants = remoteServers.Select(s => new FederatedParticipant { Uuid = Guid.NewGuid(), ServerName = s }).ToList(),
        };

    [Fact]
    public async Task Consume_FederationDisabled_NoOutboxRows()
    {
        var (context, consumer) = Create(TestHelpers.CreateConfiguration(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "false",
        }));
        await TestHelpers.EnsureActiveKeyAsync(context);

        await consumer.Consume(ConsumeContextOf(FederatedMessage("peer.test")));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_NotFederated_NoOutboxRows()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedMessage("peer.test");
        message.IsFederated = false;

        await consumer.Consume(ConsumeContextOf(message));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_NoRemoteParticipants_NoOutboxRows()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);

        await consumer.Consume(ConsumeContextOf(FederatedMessage()));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_Federated_EnqueuesNewMessageForEachRemoteServer()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedMessage("peer-a.test", "peer-b.test");

        await consumer.Consume(ConsumeContextOf(message));

        var rows = await context.Outbox.OrderBy(r => r.Destination).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.Destination).Should().Equal("peer-a.test", "peer-b.test");

        foreach (var row in rows)
        {
            row.ChatId.Should().Be(message.ChatId);
            var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
            parsed.OriginServer.Should().Be(TestHelpers.OwnServerName);
            parsed.PayloadCase.Should().Be(FederationEvent.PayloadOneofCase.NewMessage);
            parsed.NewMessage.ChatId.Should().Be(message.ChatId.ToString());
            parsed.NewMessage.FederatedMessageId.Should().Be(message.FederatedId!.Value.ToString());
            parsed.NewMessage.Sender.Uuid.Should().Be(message.SenderUuid!.Value.ToString());
            parsed.NewMessage.Sender.ServerName.Should().Be(TestHelpers.OwnServerName);
            parsed.OriginTsMs.Should().Be(message.LastChangeAt!.Value.ToUnixTimeMilliseconds());
        }
    }

    [Fact]
    public async Task Consume_FirstMessage_EnqueuesChatCreatedBeforeNewMessage()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedMessage("peer.test");
        message.IsFirstMessageInChat = true;
        message.InitiatorUuid = Guid.NewGuid();
        message.InviteeUuid = Guid.NewGuid();
        message.SenderFid = "@alice:node-a.test";

        await consumer.Consume(ConsumeContextOf(message));

        var rows = await context.Outbox.OrderBy(r => r.Id).ToListAsync();
        rows.Should().HaveCount(2);

        var chatCreated = FederationEvent.Parser.ParseFrom(rows[0].PayloadBytes);
        chatCreated.PayloadCase.Should().Be(FederationEvent.PayloadOneofCase.ChatCreated);
        chatCreated.ChatCreated.ChatId.Should().Be(message.ChatId.ToString());
        chatCreated.ChatCreated.Initiator.Uuid.Should().Be(message.InitiatorUuid!.Value.ToString());
        chatCreated.ChatCreated.Initiator.Username.Should().Be("alice");
        chatCreated.ChatCreated.Initiator.ServerName.Should().Be(TestHelpers.OwnServerName);
        chatCreated.ChatCreated.Invitee.Uuid.Should().Be(message.InviteeUuid!.Value.ToString());
        chatCreated.ChatCreated.Invitee.ServerName.Should().Be("peer.test");

        var newMessage = FederationEvent.Parser.ParseFrom(rows[1].PayloadBytes);
        newMessage.PayloadCase.Should().Be(FederationEvent.PayloadOneofCase.NewMessage);

        // У каждого события свой event_id (дедуп на приёмнике — per event).
        chatCreated.EventId.Should().NotBe(newMessage.EventId);
    }

    [Fact]
    public async Task Consume_FirstMessageWithoutUuids_OnlyNewMessage()
    {
        // IsFirstMessageInChat=true, но без Initiator/Invitee/FederatedId — ChatCreated не строится.
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedMessage("peer.test");
        message.IsFirstMessageInChat = true;
        message.FederatedId = null;

        await consumer.Consume(ConsumeContextOf(message));

        var rows = await context.Outbox.ToListAsync();
        rows.Should().HaveCount(1);

        var parsed = FederationEvent.Parser.ParseFrom(rows[0].PayloadBytes);
        parsed.PayloadCase.Should().Be(FederationEvent.PayloadOneofCase.NewMessage);
        // FederatedId не передан — генерируется новый стабильный id сообщения.
        Guid.TryParse(parsed.NewMessage.FederatedMessageId, out _).Should().BeTrue();
    }
}
