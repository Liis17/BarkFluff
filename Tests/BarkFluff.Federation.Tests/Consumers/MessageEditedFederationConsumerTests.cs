using BarkFluff.Federation.Consumers;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Federation;
using BarkFluff.Shared.Queue.Messages;

using Google.Protobuf;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Moq;

namespace BarkFluff.Federation.Tests.Consumers;

public class MessageEditedFederationConsumerTests
{
    private static (FederationContext Context, MessageEditedFederationConsumer Consumer) Create(IConfiguration? configuration = null)
    {
        var context = TestHelpers.CreateContext();
        var config = configuration ?? TestHelpers.CreateConfiguration();
        var writer = new OutboxWriter(context, TestHelpers.CreateSigningKeyService(context), config, new MetricsCollector());
        return (context, new MessageEditedFederationConsumer(writer, config, new MetricsCollector()));
    }

    private static ConsumeContext<MessageEditedEvent> ConsumeContextOf(MessageEditedEvent message)
    {
        var context = new Mock<ConsumeContext<MessageEditedEvent>>();
        context.Setup(c => c.Message).Returns(message);
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static MessageEditedEvent FederatedEdit(params string[] remoteServers)
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

        await consumer.Consume(ConsumeContextOf(FederatedEdit("peer.test")));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_NotFederated_NoOutboxRows()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedEdit("peer.test");
        message.IsFederated = false;

        await consumer.Consume(ConsumeContextOf(message));

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_Federated_EnqueuesMessageEdited()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedEdit("peer.test");

        await consumer.Consume(ConsumeContextOf(message));

        var row = await context.Outbox.SingleAsync();
        row.ChatId.Should().Be(message.ChatId);
        row.Destination.Should().Be("peer.test");

        var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
        parsed.PayloadCase.Should().Be(FederationEvent.PayloadOneofCase.MessageEdited);
        parsed.MessageEdited.ChatId.Should().Be(message.ChatId.ToString());
        parsed.MessageEdited.FederatedMessageId.Should().Be(message.FederatedId!.Value.ToString());
        parsed.OriginServer.Should().Be(TestHelpers.OwnServerName);
        parsed.OriginTsMs.Should().Be(message.LastChangeAt!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Consume_Federated_ExtractsNewTextFromWireMessage()
    {
        // Этап 2.4: текст правки извлекается из byte[] Message (сериализованный barkfluff.shared.Message),
        // как это уже делает NewMessageFederationConsumer для нового сообщения.
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedEdit("peer.test");
        message.Message = new Proto.Shared.Message
        {
            Content = new Proto.Shared.MessageContent { Text = "edited text" },
        }.ToByteArray();

        await consumer.Consume(ConsumeContextOf(message));

        var row = await context.Outbox.SingleAsync();
        var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
        parsed.MessageEdited.NewText.Should().Be("edited text");
    }

    [Fact]
    public async Task Consume_NoFederatedId_GeneratesMessageId()
    {
        var (context, consumer) = Create();
        await TestHelpers.EnsureActiveKeyAsync(context);
        var message = FederatedEdit("peer.test");
        message.FederatedId = null;

        await consumer.Consume(ConsumeContextOf(message));

        var row = await context.Outbox.SingleAsync();
        var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
        Guid.TryParse(parsed.MessageEdited.FederatedMessageId, out _).Should().BeTrue();
    }
}
