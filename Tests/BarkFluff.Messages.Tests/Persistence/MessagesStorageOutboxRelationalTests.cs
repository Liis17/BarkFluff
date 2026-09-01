using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Queue.Messages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Tests.Persistence;

public class MessagesStorageOutboxRelationalTests
{
    [Fact]
    public async Task AddMessageWithOutboxAsync_WhenEventCreationFails_RollsBackMessage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MessagesContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new MessagesContext(options);
        await context.Database.EnsureCreatedAsync();
        var storage = new MessagesStorage(context, new ChatsStorage(context));
        var message = CreateMessage(Guid.NewGuid());

        var action = async () => await storage.AddMessageWithOutboxAsync(
            message,
            _ => throw new InvalidOperationException("Cannot create event"),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        context.ChangeTracker.Clear();
        (await context.Messages.CountAsync()).Should().Be(0);
        (await context.MessageOutbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UniqueIndex_RejectsSecondMessageForSameSenderAndOperation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MessagesContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new MessagesContext(options);
        await context.Database.EnsureCreatedAsync();
        var operationId = Guid.NewGuid();

        context.Messages.AddRange(CreateMessage(operationId), CreateMessage(operationId));

        var action = async () => await context.SaveChangesAsync();
        await action.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AddMessageWithOutboxAsync_ConcurrentSameOperation_ReturnsSingleWinner()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"messages-outbox-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30,
        }.ToString();
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<MessagesContext>()
            .UseSqlite(connectionString)
            .Options;
        await using (var setup = new MessagesContext(options))
            await setup.Database.EnsureCreatedAsync();

        var operationId = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(Message Message, bool Created)> AddAsync(string text)
        {
            await using var context = new MessagesContext(options);
            var storage = new MessagesStorage(context, new ChatsStorage(context));
            var message = CreateMessage(operationId);
            message.Content!.Text = text;
            await start.Task;
            return await storage.AddMessageWithOutboxAsync(
                message,
                _ => new NewMessageEvent(),
                CancellationToken.None);
        }

        var first = AddAsync("first");
        var second = AddAsync("second");
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        results.Select(result => result.Message.Id).Distinct().Should().ContainSingle();
        results.Count(result => result.Created).Should().Be(1);
        await using var verification = new MessagesContext(options);
        (await verification.Messages.CountAsync()).Should().Be(1);
        (await verification.MessageOutbox.CountAsync()).Should().Be(1);
    }

    private static Message CreateMessage(Guid operationId)
    {
        return new Message
        {
            SenderId = 42,
            ClientOperationId = operationId,
            ChatId = Guid.NewGuid(),
            SentAt = DateTime.UtcNow,
            LastChangeAt = DateTime.UtcNow,
            Type = MessageContentType.Generic,
            ReadBy = [42],
            Content = new MessageContent { Text = "once", Attachments = [] },
        };
    }
}
