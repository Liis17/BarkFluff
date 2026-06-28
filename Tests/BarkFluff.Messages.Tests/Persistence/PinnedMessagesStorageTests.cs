using BarkFluff.Messages.Persistence.Services;

namespace BarkFluff.Messages.Tests.Persistence;

public class PinnedMessagesStorageTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task AddAsync_AndGetPinByMessageId_ReturnsPin()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedMessage(chat.Id, 1, "test");
        var pin = new Domain.PinnedMessage
        {
            ChatId = chat.Id,
            MessageId = msg.Id,
            PinnerUserId = 1,
            PinnedAt = DateTime.UtcNow
        };

        await _h.PinnedMessagesStorage.AddAsync(pin);
        await _h.PinnedMessagesStorage.SaveChangesAsync();

        var result = await _h.PinnedMessagesStorage.GetPinByMessageIdAsync(chat.Id, msg.Id);
        result.Should().NotBeNull();
        result!.PinnerUserId.Should().Be(1);
    }

    [Fact]
    public async Task GetPinByMessageId_NotFound_ReturnsNull()
    {
        var result = await _h.PinnedMessagesStorage.GetPinByMessageIdAsync(Guid.NewGuid(), 99999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListByChatAsync_ReturnsOrderedByPinnedAtDesc()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, 1, "msg1");
        var msg2 = await _h.SeedMessage(chat.Id, 1, "msg2");
        await _h.SeedPinnedMessage(chat.Id, msg1.Id, 1);
        await Task.Delay(10);
        await _h.SeedPinnedMessage(chat.Id, msg2.Id, 1);

        var result = await _h.PinnedMessagesStorage.ListByChatAsync(chat.Id, 0, 10);

        result.Should().HaveCount(2);
        result[0].PinnedAt.Should().BeAfter(result[1].PinnedAt);
    }

    [Fact]
    public async Task CountByChatAsync_ReturnsCorrectCount()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, 1, "msg1");
        var msg2 = await _h.SeedMessage(chat.Id, 1, "msg2");
        await _h.SeedPinnedMessage(chat.Id, msg1.Id, 1);
        await _h.SeedPinnedMessage(chat.Id, msg2.Id, 1);

        var count = await _h.PinnedMessagesStorage.CountByChatAsync(chat.Id);

        count.Should().Be(2);
    }

    [Fact]
    public async Task Remove_DeletesPin()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedMessage(chat.Id, 1, "msg");
        var pin = await _h.SeedPinnedMessage(chat.Id, msg.Id, 1);

        _h.PinnedMessagesStorage.Remove(pin);
        await _h.PinnedMessagesStorage.SaveChangesAsync();

        var result = await _h.PinnedMessagesStorage.GetPinByMessageIdAsync(chat.Id, msg.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAllByChatAsync_RemovesAllPins()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, 1, "msg1");
        var msg2 = await _h.SeedMessage(chat.Id, 1, "msg2");
        await _h.SeedPinnedMessage(chat.Id, msg1.Id, 1);
        await _h.SeedPinnedMessage(chat.Id, msg2.Id, 1);

        var removed = await _h.PinnedMessagesStorage.RemoveAllByChatAsync(chat.Id);
        await _h.PinnedMessagesStorage.SaveChangesAsync();

        removed.Should().Be(2);
        var count = await _h.PinnedMessagesStorage.CountByChatAsync(chat.Id);
        count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveByMessageIdAsync_RemovesAndReturnsPin()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedMessage(chat.Id, 1, "msg");
        await _h.SeedPinnedMessage(chat.Id, msg.Id, 1);

        var removed = await _h.PinnedMessagesStorage.RemoveByMessageIdAsync(msg.Id);

        removed.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveByMessageIdAsync_NotFound_ReturnsNull()
    {
        var removed = await _h.PinnedMessagesStorage.RemoveByMessageIdAsync(99999);

        removed.Should().BeNull();
    }
}
