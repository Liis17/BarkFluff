using BarkFluff.Messages.Persistence.Services;

namespace BarkFluff.Messages.Tests.Persistence;

public class MessagesStorageTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task AddMessage_SavesAndReturnsMessage()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var message = new Domain.Message
        {
            ChatId = chat.Id,
            SenderId = 1,
            SentAt = DateTime.UtcNow,
            Type = Domain.MessageContentType.Generic,
            ReadBy = [1],
            Content = new Domain.MessageContent { Text = "hello" }
        };

        var result = await _h.MessagesStorage.AddMessage(message);

        result.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetMessageById_Existing_ReturnsMessage()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedMessage(chat.Id, 1, "test");

        var result = await _h.MessagesStorage.GetMessageById(msg.Id);

        result.Should().NotBeNull();
        result!.Content!.Text.Should().Be("test");
    }

    [Fact]
    public async Task GetMessageById_NonExistent_ReturnsNull()
    {
        var result = await _h.MessagesStorage.GetMessageById(99999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMessagesByIds_ReturnsMatchingMessages()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, 1, "msg1");
        var msg2 = await _h.SeedMessage(chat.Id, 1, "msg2");
        await _h.SeedMessage(chat.Id, 1, "msg3");

        var result = await _h.MessagesStorage.GetMessagesByIds([msg1.Id, msg2.Id]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMessagesByIds_ExcludesDeleted()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, 1, "visible");
        var msg2 = await _h.SeedMessage(chat.Id, 1, "deleted", isDeleted: true);

        var result = await _h.MessagesStorage.GetMessagesByIds([msg1.Id, msg2.Id]);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetMessagesByIdsInChatAsync_EmptyIds_ReturnsEmpty()
    {
        var result = await _h.MessagesStorage.GetMessagesByIdsInChatAsync(Guid.NewGuid(), []);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChatMessages_ReturnsMessagesPaginated()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        for (int i = 0; i < 5; i++)
            await _h.SeedMessage(chat.Id, 1, $"msg{i}");

        var result = await _h.MessagesStorage.GetChatMessages(chat.Id, null, 3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetChatMessages_ExcludesDeleted()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        await _h.SeedMessage(chat.Id, 1, "visible");
        await _h.SeedMessage(chat.Id, 1, "deleted", isDeleted: true);

        var result = await _h.MessagesStorage.GetChatMessages(chat.Id, null, 10);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetChatMessagesWithOffset_NoFromMessage_ReturnsLatest()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        for (int i = 0; i < 5; i++)
            await _h.SeedMessage(chat.Id, 1, $"msg{i}");

        var result = await _h.MessagesStorage.GetChatMessagesWithOffset(chat.Id, null, 3, 0);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetChatMessagesWithOffset_WithFromMessage_ReturnsBidirectional()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var old = await _h.SeedMessage(chat.Id, 1, "old");
        var mid = await _h.SeedMessage(chat.Id, 1, "middle");
        var recent = await _h.SeedMessage(chat.Id, 1, "recent");

        var result = await _h.MessagesStorage.GetChatMessagesWithOffset(chat.Id, mid.Id, 5, 5);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task SaveChangesAsync_UpdatesTrackedEntity()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedMessage(chat.Id, 1, "original");

        var tracked = await _h.MessagesStorage.GetMessageById(msg.Id);
        tracked!.IsDeleted = true;
        await _h.MessagesStorage.SaveChangesAsync();

        var reloaded = await _h.MessagesStorage.GetMessageById(msg.Id);
        reloaded!.IsDeleted.Should().BeTrue();
    }
}
