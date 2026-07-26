using BarkFluff.Federation.Consumers;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Messages;

using MassTransit;

using Moq;

namespace BarkFluff.Federation.Tests.Consumers;

public class PresenceStatusChangedConsumerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private static ConsumeContext<OnlineStatusChangedEvent> Context(OnlineStatusChangedEvent message)
    {
        var context = new Mock<ConsumeContext<OnlineStatusChangedEvent>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static (PresenceStatusChangedConsumer Consumer, IncomingPresenceRegistry Registry) Create(
        bool federationEnabled = true)
    {
        var configuration = TestHelpers.CreateConfiguration(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = federationEnabled ? "true" : "false",
        });

        var registry = new IncomingPresenceRegistry();
        var consumer = new PresenceStatusChangedConsumer(
            registry, new FederationSwitch(configuration), new MetricsCollector());

        return (consumer, registry);
    }

    [Fact]
    public async Task LocalStatusChange_MarksWatchingSubscription()
    {
        var (consumer, registry) = Create();
        var subscription = registry.Add("node-b.test", new Dictionary<long, Guid> { [10] = Guid.NewGuid() });

        await consumer.Consume(Context(new OnlineStatusChangedEvent
        {
            UserId = 10,
            Status = 1,
            LastSeen = DateTime.UtcNow,
        }));

        subscription.TakeDue(DateTime.UtcNow, Window).Should().BeEquivalentTo([10L]);
    }

    [Fact]
    public async Task RemoteStatusChange_IsIgnored()
    {
        // Событие про remote-пользователя само пришло из федерации. Пересылать его обратно
        // нельзя — нода говорит только за своих.
        var (consumer, registry) = Create();
        var subscription = registry.Add("node-b.test", new Dictionary<long, Guid> { [10] = Guid.NewGuid() });

        await consumer.Consume(Context(new OnlineStatusChangedEvent
        {
            UserId = 0,
            UserUuid = Guid.NewGuid(),
            Status = 1,
            LastSeen = DateTime.UtcNow,
        }));

        subscription.TakeDue(DateTime.UtcNow, Window).Should().BeEmpty();
    }

    [Fact]
    public async Task FederationDisabled_ConsumerDoesNothing()
    {
        var (consumer, registry) = Create(federationEnabled: false);
        var subscription = registry.Add("node-b.test", new Dictionary<long, Guid> { [10] = Guid.NewGuid() });

        await consumer.Consume(Context(new OnlineStatusChangedEvent
        {
            UserId = 10,
            Status = 1,
            LastSeen = DateTime.UtcNow,
        }));

        subscription.TakeDue(DateTime.UtcNow, Window).Should().BeEmpty();
    }
}
