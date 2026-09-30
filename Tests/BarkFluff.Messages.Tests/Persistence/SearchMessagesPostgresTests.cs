using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Messages.Persistence.Services.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BarkFluff.Messages.Features.SearchMessages;
using BarkFluff.Proto.Users;

namespace BarkFluff.Messages.Tests.Persistence;

public class SearchMessagesPostgresTests
{
    [SearchPostgresFact]
    public async Task Search_FindsLiteralCaseInsensitiveTextOnlyInVisibleRegularChats()
    {
        await using var database = await SearchDatabase.Create();
        var context = database.Context;
        var visible = Chat(1);
        var otherUser = Chat(99);
        var hidden = Chat(1);
        hidden.Members![0].HiddenAt = DateTime.UtcNow;
        var privateChat = Chat(1);
        privateChat.Type = ChatType.Private;
        context.Chats.AddRange(visible, otherUser, hidden, privateChat);
        var expected = Message(visible.Id, "ПЛАН 100%_готов");
        context.Messages.AddRange(
            expected,
            Message(visible.Id, "план 100XXготов"),
            Message(otherUser.Id, "план 100%_готов"),
            Message(hidden.Id, "план 100%_готов"),
            Message(privateChat.Id, "план 100%_готов"),
            Message(visible.Id, "план 100%_готов", deleted: true),
            Message(visible.Id, "план 100%_готов", system: true));
        await context.SaveChangesAsync();

        var storage = new MessagesStorage(context, new ChatsStorage(context));
        var found = await storage.SearchMessages(1, new MessageSearchFilter { Text = "план 100%_" });

        found.Select(m => m.Id).Should().Equal(expected.Id);
    }

    private static Chat Chat(long userId) => new()
    {
        Id = Guid.NewGuid(),
        Members = [new ChatMember { UserId = userId, JoinedAt = DateTime.UtcNow }],
    };

    [SearchPostgresFact]
    public async Task Search_CombinesRemoteAuthorDatesAndAttachmentTypesWithStableCursor()
    {
        await using var database = await SearchDatabase.Create();
        var context = database.Context;
        var chat = Chat(1);
        context.Chats.Add(chat);
        var author = Guid.NewGuid();
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = from.AddDays(1);
        var older = Message(chat.Id, "Отчёт");
        var newer = Message(chat.Id, "Отчёт");
        foreach (var message in new[] { older, newer })
        {
            message.SenderId = null;
            message.SenderUuid = author;
            message.SentAt = from;
            message.Content!.Attachments = [
                new MessageAttachment { Type = MessageAttachmentType.Image },
                new MessageAttachment { Type = MessageAttachmentType.Voice },
            ];
        }
        var atEnd = Message(chat.Id, "Отчёт");
        atEnd.SenderId = null;
        atEnd.SenderUuid = author;
        atEnd.SentAt = before;
        atEnd.Content!.Attachments = [new MessageAttachment { Type = MessageAttachmentType.Image }];
        var otherAuthor = Message(chat.Id, "Отчёт");
        otherAuthor.SenderId = null;
        otherAuthor.SenderUuid = Guid.NewGuid();
        otherAuthor.SentAt = from;
        otherAuthor.Content!.Attachments = [new MessageAttachment { Type = MessageAttachmentType.Image }];
        var noAttachments = Message(chat.Id, "Отчёт");
        noAttachments.SenderId = null;
        noAttachments.SenderUuid = author;
        noAttachments.SentAt = from;
        context.Messages.AddRange(older, newer, atEnd, otherAuthor, noAttachments);
        await context.SaveChangesAsync();
        var storage = new MessagesStorage(context, new ChatsStorage(context));
        var filter = new MessageSearchFilter
        {
            Text = "отчёт", AuthorUserUuid = author, SentFrom = from, SentBefore = before,
            HasAttachments = true, AttachmentTypes = [MessageAttachmentType.Image, MessageAttachmentType.Voice], PageSize = 1,
        };

        var first = await storage.SearchMessages(1, filter);
        var second = await storage.SearchMessages(1, filter with { CursorSentAt = first[0].SentAt, CursorMessageId = first[0].Id });

        first.Select(message => message.Id).Should().Equal(newer.Id, older.Id);
        second.Select(message => message.Id).Should().Equal(older.Id);

        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new ListByIdsResponse
            {
                Users = { new User { Id = 1, FirstName = "Я" } },
            }));
        users.Setup(client => client.GetUsersByUuidAsync(It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new GetUsersByUuidResponse
            {
                Users = { new UserProfileByUuid { Uuid = author.ToString(), Found = true, FirstName = "Автор" } },
            }));
        var helper = new TestHelper();
        var handler = new SearchMessagesQueryHandler(helper.CreateUserContext(1), storage, users.Object);
        var page = await handler.Handle(new SearchMessagesQuery { Filter = filter }, CancellationToken.None);
        page.Hits.Should().ContainSingle();
        page.Hits[0].Author.UserUuid.Should().Be(author.ToString());
        page.Hits[0].Author.DisplayName.Should().Be("Автор");
        page.NextCursor.MessageId.Should().Be(newer.Id);
    }

    private static Message Message(Guid chatId, string text, bool deleted = false, bool system = false) => new()
    {
        ChatId = chatId,
        SenderId = 1,
        SentAt = DateTime.UtcNow.AddMinutes(-1),
        LastChangeAt = DateTime.UtcNow,
        ReadBy = [],
        IsDeleted = deleted,
        Type = system ? MessageContentType.System : MessageContentType.Generic,
        Content = new MessageContent { Text = text, Attachments = [] },
    };

    private sealed class SearchDatabase : IAsyncDisposable
    {
        private readonly string _rootConnectionString;
        private readonly string _name;
        public MessagesContext Context { get; }

        private SearchDatabase(string rootConnectionString, string name, MessagesContext context)
        {
            _rootConnectionString = rootConnectionString;
            _name = name;
            Context = context;
        }

        public static async Task<SearchDatabase> Create()
        {
            var root = Environment.GetEnvironmentVariable("BARKFLUFF_SEARCH_POSTGRES")!;
            var name = "barkfluff_search_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(root);
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection))
                await command.ExecuteNonQueryAsync();
            var builder = new NpgsqlConnectionStringBuilder(root) { Database = name, Pooling = false };
            var context = new MessagesContext(new DbContextOptionsBuilder<MessagesContext>()
                .UseNpgsql(builder.ConnectionString).Options);
            await context.Database.EnsureCreatedAsync();
            return new SearchDatabase(root, name, context);
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

public sealed class SearchPostgresFactAttribute : FactAttribute
{
    public SearchPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BARKFLUFF_SEARCH_POSTGRES")))
            Skip = "Set BARKFLUFF_SEARCH_POSTGRES to a disposable PostgreSQL instance with CREATE DATABASE access.";
    }
}
