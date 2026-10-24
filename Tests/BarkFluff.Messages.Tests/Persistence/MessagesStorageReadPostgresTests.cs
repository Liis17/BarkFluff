using System.Text.Json;

using BarkFluff.Messages.BackgroundServices;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.Federation;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace BarkFluff.Messages.Tests.Persistence;

public class MessagesStorageReadPostgresTests
{
    [SearchPostgresFact]
    public async Task ConcurrentRangeReadsSerializeAndEmitOneOutboxRowPerActualChange()
    {
        await using var database = await PostgresDatabase.Create();
        var (chatId, cutoff) = await SeedUnreadMessages(database.Context, 102);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(int ChangedCount, bool HasMore)> ReadBatchAsync()
        {
            await using var context = database.CreateContext();
            var storage = new MessagesStorage(context, new ChatsStorage(context));
            await start.Task;
            return await storage.MarkChatMessagesAsReadUpToWithOutboxAsync(chatId, cutoff, 1);
        }

        var first = ReadBatchAsync();
        var second = ReadBatchAsync();
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        results.Sum(result => result.ChangedCount).Should().Be(102);
        results.Count(result => result.HasMore).Should().Be(1);
        await using var verification = database.CreateContext();
        var messages = await verification.Messages.Where(message => message.ChatId == chatId).ToListAsync();
        messages.Should().HaveCount(102).And.OnlyContain(message => message.ReadBy.SequenceEqual(new[] { 1L }));
        var rows = await verification.MessageOutbox.Where(entry => entry.EventKind == MessageOutboxEventKind.ReadBy).ToListAsync();
        rows.Should().HaveCount(102);
        rows.Select(row => row.MessageId).Distinct().Should().HaveCount(102);
    }

    [SearchPostgresFact]
    public async Task ReadUpdateRollsBackWhenTransactionalOutboxSaveFails()
    {
        await using var database = await PostgresDatabase.Create();
        var (chatId, cutoff) = await SeedUnreadMessages(database.Context, 1);
        var options = new DbContextOptionsBuilder<MessagesContext>()
            .UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3))
            .AddInterceptors(new RejectReadOutboxInterceptor())
            .Options;
        await using (var context = new MessagesContext(options))
        {
            var storage = new MessagesStorage(context, new ChatsStorage(context));
            var update = () => storage.MarkChatMessagesAsReadUpToWithOutboxAsync(chatId, cutoff, 1);
            await update.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Read outbox insert rejected for rollback test");
        }

        await using var verification = database.CreateContext();
        var message = await verification.Messages.SingleAsync(item => item.ChatId == chatId);
        message.ReadBy.Should().BeEmpty();
        (await verification.MessageOutbox.CountAsync()).Should().Be(0);
    }

    [SearchPostgresFact]
    public async Task RetryEnabledRangeReadPreservesContentTimestampAndRepeatedCallDoesNotDuplicateOutbox()
    {
        await using var database = await PostgresDatabase.Create();
        var (chatId, cutoff) = await SeedUnreadMessages(database.Context, 1);
        var before = await database.Context.Messages.AsNoTracking()
            .Where(message => message.ChatId == chatId)
            .Select(message => message.LastChangeAt)
            .SingleAsync();

        await using var context = database.CreateContext();
        var storage = new MessagesStorage(context, new ChatsStorage(context));
        (await storage.MarkChatMessagesAsReadUpToWithOutboxAsync(chatId, cutoff, 1)).Should().Be((1, false));
        (await storage.MarkChatMessagesAsReadUpToWithOutboxAsync(chatId, cutoff, 1)).Should().Be((0, false));

        await using var verification = database.CreateContext();
        var message = await verification.Messages.AsNoTracking().SingleAsync(item => item.ChatId == chatId);
        message.ReadBy.Should().ContainSingle().Which.Should().Be(1);
        message.LastChangeAt.Should().Be(before);
        (await verification.MessageOutbox.CountAsync(entry => entry.EventKind == MessageOutboxEventKind.ReadBy))
            .Should().Be(1);
    }

    [SearchPostgresFact]
    public async Task RetryEnabledExactReadUsesExecutionStrategyAndPreservesContentTimestamp()
    {
        await using var database = await PostgresDatabase.Create();
        var (chatId, cutoff) = await SeedUnreadMessages(database.Context, 1);
        var before = await database.Context.Messages.AsNoTracking()
            .Where(message => message.Id == cutoff)
            .Select(message => message.LastChangeAt)
            .SingleAsync();

        await using var context = database.CreateContext();
        var storage = new MessagesStorage(context, new ChatsStorage(context));
        (await storage.MarkMessagesAsReadWithOutboxAsync([cutoff], 1)).Should().Be(1);

        await using var verification = database.CreateContext();
        var message = await verification.Messages.AsNoTracking().SingleAsync(item => item.Id == cutoff);
        message.ReadBy.Should().ContainSingle().Which.Should().Be(1);
        message.LastChangeAt.Should().Be(before);
        (await verification.MessageOutbox.CountAsync(entry => entry.MessageId == cutoff &&
            entry.EventKind == MessageOutboxEventKind.ReadBy)).Should().Be(1);
    }

    [SearchPostgresFact]
    public async Task ReadOutboxDispatcherRetriesPersistedRowAfterBrokerFailure()
    {
        await using var database = await PostgresDatabase.Create();
        var eventId = Guid.NewGuid();
        var readEvent = new MessageReadEvent { ChatId = Guid.NewGuid(), MessageId = 77, NewReadBy = [1], NewReaders = [1] };
        var now = DateTime.UtcNow;
        database.Context.MessageOutbox.Add(new MessageOutboxEntry
        {
            EventId = eventId,
            MessageId = readEvent.MessageId,
            EventKind = MessageOutboxEventKind.ReadBy,
            Payload = JsonSerializer.SerializeToUtf8Bytes(readEvent),
            CreatedAt = now,
            NextAttemptAt = now,
        });
        await database.Context.SaveChangesAsync();

        var publishAttempts = 0;
        var publisher = new Mock<IPublishEndpoint>();
        publisher.Setup(endpoint => endpoint.Publish(
                It.IsAny<MessageReadEvent>(),
                It.IsAny<IPipe<PublishContext<MessageReadEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => ++publishAttempts == 1
                ? Task.FromException(new InvalidOperationException("RabbitMQ unavailable"))
                : Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddDbContext<MessagesContext>(options =>
            options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<MessageQueueSender>();
        services.AddSingleton(publisher.Object);
        await using var provider = services.BuildServiceProvider();
        var dispatcher = new MessageOutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(), new MetricsCollector(),
            TestHelper.CreateLogger<MessageOutboxDispatcher>());

        await dispatcher.DispatchOnceAsync(CancellationToken.None);
        await using (var verifyRetry = database.CreateContext())
        {
            var retry = await verifyRetry.MessageOutbox.SingleAsync();
            retry.Status.Should().Be(MessageOutboxStatus.Pending);
            retry.Attempts.Should().Be(1);
            retry.EventId.Should().Be(eventId);
            retry.NextAttemptAt.Should().BeAfter(now);
            retry.LastError.Should().Contain("RabbitMQ unavailable");
            retry.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await verifyRetry.SaveChangesAsync();
        }

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        await using var deliveredContext = database.CreateContext();
        var delivered = await deliveredContext.MessageOutbox.SingleAsync();
        delivered.Status.Should().Be(MessageOutboxStatus.Delivered);
        delivered.Attempts.Should().Be(1);
        delivered.EventId.Should().Be(eventId);
        publishAttempts.Should().Be(2);
    }

    [SearchPostgresFact]
    public async Task FederatedRangeAnchorUsesLatestSentAtAcrossBatchesInsteadOfLargestLocalId()
    {
        await using var database = await PostgresDatabase.Create();
        var localUuid = Guid.NewGuid();
        var remoteUuid = Guid.NewGuid();
        var (low, high) = FederatedUuidPair.Normalize(localUuid, remoteUuid);
        var chat = new Chat
        {
            Type = ChatType.Regular,
            IsFederated = true,
            FederatedStatus = FederatedStatus.Active,
            FederatedUuidLow = low,
            FederatedUuidHigh = high,
            Members =
            [
                new ChatMember { UserId = 1, UserUuid = localUuid, JoinedAt = DateTime.UtcNow },
                new ChatMember { UserId = null, UserUuid = remoteUuid, ServerName = "peer.test", JoinedAt = DateTime.UtcNow },
            ],
        };
        database.Context.Chats.Add(chat);
        await database.Context.SaveChangesAsync();
        var latestByTime = FederatedMessage(chat.Id, Guid.NewGuid(), remoteUuid, DateTime.UtcNow.AddDays(1));
        var insertedLaterButOlder = FederatedMessage(chat.Id, Guid.NewGuid(), remoteUuid, DateTime.UtcNow.AddDays(-1));
        database.Context.Messages.AddRange(latestByTime, insertedLaterButOlder);
        await database.Context.SaveChangesAsync();
        latestByTime.Id.Should().BeLessThan(insertedLaterButOlder.Id);
        var storage = new MessagesStorage(database.Context, new ChatsStorage(database.Context));

        var firstBatch = await storage.MarkChatMessagesAsReadUpToWithOutboxAsync(chat.Id, latestByTime.Id, 1);
        var secondBatch = await storage.MarkChatMessagesAsReadUpToWithOutboxAsync(chat.Id, insertedLaterButOlder.Id, 1);

        firstBatch.Should().Be((1, false));
        secondBatch.Should().Be((1, false));
        var events = await database.Context.MessageOutbox.OrderBy(row => row.Id).ToListAsync();
        events.Should().HaveCount(2);
        var firstRead = JsonSerializer.Deserialize<MessageReadEvent>(events[0].Payload)!;
        var secondRead = JsonSerializer.Deserialize<MessageReadEvent>(events[1].Payload)!;
        firstRead.IsFederated.Should().BeTrue();
        firstRead.UpToFederatedMessageId.Should().Be(latestByTime.FederatedId);
        secondRead.MessageId.Should().Be(insertedLaterButOlder.Id);
        secondRead.IsFederated.Should().BeTrue();
        secondRead.UpToFederatedMessageId.Should().Be(latestByTime.FederatedId);
        secondRead.ReaderUuid.Should().Be(localUuid);
        secondRead.RemoteParticipants.Should().ContainSingle(participant => participant.Uuid == remoteUuid);
        (await database.Context.FederatedReadStates.CountAsync()).Should().Be(0);
    }

    private static async Task<(Guid ChatId, long Cutoff)> SeedUnreadMessages(MessagesContext context, int count)
    {
        var chat = new Chat
        {
            Type = ChatType.Regular,
            Members = [new ChatMember { UserId = 1, JoinedAt = DateTime.UtcNow }],
        };
        context.Chats.Add(chat);
        await context.SaveChangesAsync();
        var messages = Enumerable.Range(0, count).Select(index => new Message
        {
            ChatId = chat.Id,
            SenderId = 2,
            SentAt = DateTime.UtcNow.AddSeconds(index),
            LastChangeAt = DateTime.UtcNow,
            Type = MessageContentType.Generic,
            ReadBy = [],
            Content = new MessageContent { Text = $"unread-{index}", Attachments = [] },
        }).ToArray();
        context.Messages.AddRange(messages);
        await context.SaveChangesAsync();
        return (chat.Id, messages.Max(message => message.Id));
    }

    private static Message FederatedMessage(Guid chatId, Guid federatedId, Guid senderUuid, DateTime sentAt) => new()
    {
        ChatId = chatId,
        SenderUuid = senderUuid,
        FederatedId = federatedId,
        SentAt = sentAt,
        LastChangeAt = sentAt,
        Type = MessageContentType.Generic,
        ReadBy = [],
        Content = new MessageContent { Text = "federated message", Attachments = [] },
    };

    private sealed class RejectReadOutboxInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<MessageOutboxEntry>()
                    .Any(entry => entry.State == EntityState.Added) == true)
                throw new InvalidOperationException("Read outbox insert rejected for rollback test");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class PostgresDatabase : IAsyncDisposable
    {
        private readonly string _rootConnectionString;
        private readonly string _name;

        public string ConnectionString { get; }
        public MessagesContext Context { get; }

        private PostgresDatabase(string rootConnectionString, string name, MessagesContext context, string connectionString)
        {
            _rootConnectionString = rootConnectionString;
            _name = name;
            Context = context;
            ConnectionString = connectionString;
        }

        public MessagesContext CreateContext() => new(new DbContextOptionsBuilder<MessagesContext>()
            .UseNpgsql(ConnectionString, options => options.EnableRetryOnFailure(3)).Options);

        public static async Task<PostgresDatabase> Create()
        {
            var root = Environment.GetEnvironmentVariable("BARKFLUFF_SEARCH_POSTGRES")!;
            var name = "barkfluff_reads_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(root);
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection))
                await command.ExecuteNonQueryAsync();
            var builder = new NpgsqlConnectionStringBuilder(root) { Database = name, Pooling = false };
            var context = new MessagesContext(new DbContextOptionsBuilder<MessagesContext>()
                .UseNpgsql(builder.ConnectionString, options => options.EnableRetryOnFailure(3)).Options);
            await context.Database.EnsureCreatedAsync();
            return new PostgresDatabase(root, name, context, builder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await using var connection = new NpgsqlConnection(_rootConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{_name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
