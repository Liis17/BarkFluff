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

public class MessageDeletedFederationConsumerTests
{
    private static (FederationContext Context, MessageDeletedFederationConsumer Consumer) Create(IConfiguration? configuration = null)
    {
        var context = TestHelpers.CreateContext();
        var config = configuration ?? TestHelpers.CreateConfiguration();
        var writer = new OutboxWriter(context, TestHelpers.CreateSigningKeyService(context), config, new MetricsCollector());
        return (context, new MessageDeletedFederationConsumer(writer, config, new MetricsCollector()));
    }

    private static ConsumeContext<MessageDeletedEvent> ConsumeContextOf(MessageDeletedEvent message)
    {
        var context = new Mock<ConsumeContext<MessageDeletedEvent>>();
        context.Setup(c => c.Message).Returns(message);
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static MessageDeletedEvent FederatedDelete(params string[] remoteServers)
        => new()
        {
            ChatId = Guid.NewGuid(),
            IsFederated = true,
            FederatedId = Guid.NewGuid(),
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

        await consumer.Consume(ConsumeContextOf(FederatedDelete("peer.test")));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_NotFederated_NoOutboxRows()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedDelete("peer.test");
        message.IsFederated = false;

        await consumer.Consume(ConsumeContextOf(message));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_Federated_EnqueuesMessageDeleted()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedDelete("peer.test");

        await consumer.Consume(ConsumeContextOf(message));

        var row = await context.Outbox.SingleAsync();
        row.ChatId.Should().Be(message.ChatId);
        row.Destination.Should().Be("peer.test");

        var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
        parsed.PayloadCase.Should().Be(FederationEvent.PayloadOneofCase.MessageDeleted);
        parsed.MessageDeleted.ChatId.Should().Be(message.ChatId.ToString());
        parsed.MessageDeleted.FederatedMessageId.Should().Be(message.FederatedId!.Value.ToString());
        parsed.OriginServer.Should().Be(TestHelpers.OwnServerName);
    }
}
