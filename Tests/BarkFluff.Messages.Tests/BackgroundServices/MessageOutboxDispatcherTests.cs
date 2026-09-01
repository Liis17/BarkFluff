using System.Text.Json;

using BarkFluff.Messages.BackgroundServices;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.Messages.Tests.BackgroundServices;

public class MessageOutboxDispatcherTests
{
    [Fact]
    public async Task DispatchOnceAsync_PublishesWithStableEventIdAndMarksDelivered()
    {
        var eventId = Guid.NewGuid();
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<NewMessageEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider(publishEndpoint.Object);
        var rowId = await SeedOutboxAsync(provider, eventId, MessageOutboxStatus.Pending, DateTime.UtcNow);
        var dispatcher = CreateDispatcher(provider);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<MessagesContext>()
            .MessageOutbox.SingleAsync(entry => entry.Id == rowId);
        row.Status.Should().Be(MessageOutboxStatus.Delivered);
        publishEndpoint.Verify(endpoint => endpoint.Publish(
            It.Is<NewMessageEvent>(message => message.EventId == eventId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchOnceAsync_WhenPublishFails_SchedulesRetry()
    {
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<NewMessageEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ unavailable"));
        await using var provider = CreateProvider(publishEndpoint.Object);
        var before = DateTime.UtcNow;
        var rowId = await SeedOutboxAsync(provider, Guid.NewGuid(), MessageOutboxStatus.Pending, before);
        var dispatcher = CreateDispatcher(provider);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<MessagesContext>()
            .MessageOutbox.SingleAsync(entry => entry.Id == rowId);
        row.Status.Should().Be(MessageOutboxStatus.Pending);
        row.Attempts.Should().Be(1);
        row.NextAttemptAt.Should().BeAfter(before);
        row.LastError.Should().Contain("RabbitMQ unavailable");
    }

    [Fact]
    public async Task DispatchOnceAsync_ReclaimsExpiredProcessingLease()
    {
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<NewMessageEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider(publishEndpoint.Object);
        var rowId = await SeedOutboxAsync(
            provider,
            Guid.NewGuid(),
            MessageOutboxStatus.Processing,
            DateTime.UtcNow.AddMinutes(-1));
        var dispatcher = CreateDispatcher(provider);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<MessagesContext>()
            .MessageOutbox.SingleAsync(entry => entry.Id == rowId);
        row.Status.Should().Be(MessageOutboxStatus.Delivered);
    }

    [Fact]
    public async Task DispatchOnceAsync_AfterBrokerRecovery_DeliversRetriedEvent()
    {
        var brokerAvailable = false;
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<NewMessageEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => brokerAvailable
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException("RabbitMQ unavailable")));
        await using var provider = CreateProvider(publishEndpoint.Object);
        var rowId = await SeedOutboxAsync(
            provider,
            Guid.NewGuid(),
            MessageOutboxStatus.Pending,
            DateTime.UtcNow);
        var dispatcher = CreateDispatcher(provider);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        await using (var retryScope = provider.CreateAsyncScope())
        {
            var context = retryScope.ServiceProvider.GetRequiredService<MessagesContext>();
            var retry = await context.MessageOutbox.SingleAsync(entry => entry.Id == rowId);
            retry.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync();
        }
        brokerAvailable = true;

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        await using var verificationScope = provider.CreateAsyncScope();
        var delivered = await verificationScope.ServiceProvider.GetRequiredService<MessagesContext>()
            .MessageOutbox.SingleAsync(entry => entry.Id == rowId);
        delivered.Status.Should().Be(MessageOutboxStatus.Delivered);
        delivered.Attempts.Should().Be(1);
        publishEndpoint.Verify(endpoint => endpoint.Publish(
            It.IsAny<NewMessageEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DispatchOnceAsync_AfterRetryWindow_MarksEventDeadLetter()
    {
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<NewMessageEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ unavailable"));
        await using var provider = CreateProvider(publishEndpoint.Object);
        var rowId = await SeedOutboxAsync(
            provider,
            Guid.NewGuid(),
            MessageOutboxStatus.Pending,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-8));

        await CreateDispatcher(provider).DispatchOnceAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<MessagesContext>()
            .MessageOutbox.SingleAsync(entry => entry.Id == rowId);
        row.Status.Should().Be(MessageOutboxStatus.DeadLetter);
        row.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Janitor_RemovesOnlyDeliveredEventsOutsideRetentionWindow()
    {
        await using var provider = CreateProvider(Mock.Of<IPublishEndpoint>());
        var expiredId = await SeedOutboxAsync(
            provider,
            Guid.NewGuid(),
            MessageOutboxStatus.Delivered,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-8));
        var currentId = await SeedOutboxAsync(
            provider,
            Guid.NewGuid(),
            MessageOutboxStatus.Delivered,
            DateTime.UtcNow);
        var deadLetterId = await SeedOutboxAsync(
            provider,
            Guid.NewGuid(),
            MessageOutboxStatus.DeadLetter,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-8));
        var janitor = new MessageOutboxJanitor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TestHelper.CreateLogger<MessageOutboxJanitor>());

        var deleted = await janitor.CleanupOnceAsync(CancellationToken.None);

        deleted.Should().Be(1);
        await using var scope = provider.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<MessagesContext>()
            .MessageOutbox.Select(entry => entry.Id).ToListAsync();
        ids.Should().NotContain(expiredId).And.Contain([currentId, deadLetterId]);
    }

    private static ServiceProvider CreateProvider(IPublishEndpoint publishEndpoint)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<MessagesContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddScoped<MessageQueueSender>();
        services.AddSingleton(publishEndpoint);
        return services.BuildServiceProvider();
    }

    private static MessageOutboxDispatcher CreateDispatcher(ServiceProvider provider)
    {
        return new MessageOutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MetricsCollector(),
            TestHelper.CreateLogger<MessageOutboxDispatcher>());
    }

    private static async Task<long> SeedOutboxAsync(
        ServiceProvider provider,
        Guid eventId,
        MessageOutboxStatus status,
        DateTime nextAttemptAt,
        DateTime? createdAt = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MessagesContext>();
        var row = new MessageOutboxEntry
        {
            EventId = eventId,
            MessageId = Random.Shared.NextInt64(1, long.MaxValue),
            Payload = JsonSerializer.SerializeToUtf8Bytes(new NewMessageEvent { EventId = eventId }),
            CreatedAt = createdAt ?? DateTime.UtcNow,
            NextAttemptAt = nextAttemptAt,
            Status = status,
        };
        context.MessageOutbox.Add(row);
        await context.SaveChangesAsync();
        return row.Id;
    }
}
