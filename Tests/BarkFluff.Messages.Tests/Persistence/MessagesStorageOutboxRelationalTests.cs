using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;

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
