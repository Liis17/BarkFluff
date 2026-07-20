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

public class MessageReadFederationConsumerTests
{
    private static (FederationContext Context, MessageReadFederationConsumer Consumer) Create(IConfiguration? configuration = null)
    {
        var context = TestHelpers.CreateContext();
        var config = configuration ?? TestHelpers.CreateConfiguration();
        var writer = new OutboxWriter(context, TestHelpers.CreateSigningKeyService(context), config, new MetricsCollector());
        return (context, new MessageReadFederationConsumer(writer, config, new MetricsCollector()));
    }

    private static ConsumeContext<MessageReadEvent> ConsumeContextOf(MessageReadEvent message)
    {
        var context = new Mock<ConsumeContext<MessageReadEvent>>();
        context.Setup(c => c.Message).Returns(message);
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static MessageReadEvent FederatedRead(params string[] remoteServers)
        => new()
        {
            ChatId = Guid.NewGuid(),
            IsFederated = true,
            ReaderUuid = Guid.NewGuid(),
            UpToFederatedMessageId = Guid.NewGuid(),
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

        await consumer.Consume(ConsumeContextOf(FederatedRead("peer.test")));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_NotFederated_NoOutboxRows()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedRead("peer.test");
        message.IsFederated = false;

        await consumer.Consume(ConsumeContextOf(message));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_Federated_EnqueuesMessagesRead()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedRead("peer.test");

        await consumer.Consume(ConsumeContextOf(message));

        var row = await context.Outbox.SingleAsync();
        row.ChatId.Should().Be(message.ChatId);
        row.Destination.Should().Be("peer.test");

        var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
        parsed.PayloadCase.Should().Be(FederationEvent.PayloadOneofCase.MessagesRead);
        parsed.MessagesRead.ChatId.Should().Be(message.ChatId.ToString());
        parsed.MessagesRead.ReaderUuid.Should().Be(message.ReaderUuid!.Value.ToString());
        parsed.MessagesRead.UpToFederatedMessageId.Should().Be(message.UpToFederatedMessageId!.Value.ToString());
        parsed.OriginServer.Should().Be(TestHelpers.OwnServerName);
    }

    [Fact]
    public async Task Consume_NoUuids_EmptyGuidsInPayload()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedRead("peer.test");
        message.ReaderUuid = null;
        message.UpToFederatedMessageId = null;

        await consumer.Consume(ConsumeContextOf(message));

        var row = await context.Outbox.SingleAsync();
        var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
        parsed.MessagesRead.ReaderUuid.Should().Be(Guid.Empty.ToString());
        parsed.MessagesRead.UpToFederatedMessageId.Should().Be(Guid.Empty.ToString());
    }
}
