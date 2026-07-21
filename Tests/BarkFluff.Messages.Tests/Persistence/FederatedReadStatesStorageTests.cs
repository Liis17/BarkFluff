namespace BarkFluff.Messages.Tests.Persistence;

public class FederatedReadStatesStorageTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task UpsertAsync_NoExisting_Inserts()
    {
        var chatId = Guid.NewGuid();
        var userUuid = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var readAt = DateTime.UtcNow;

        var applied = await _h.FederatedReadStatesStorage.UpsertAsync(chatId, userUuid, messageId, readAt);

        applied.Should().BeTrue();
        var stored = (await _h.FederatedReadStatesStorage.GetForChatAsync(chatId)).Single();
        stored.UserUuid.Should().Be(userUuid);
        stored.LastReadFederatedMessageId.Should().Be(messageId);
        stored.ReadAt.Should().Be(readAt);
    }

    [Fact]
    public async Task UpsertAsync_NewerRead_Applies()
    {
        var chatId = Guid.NewGuid();
        var userUuid = Guid.NewGuid();
        await _h.FederatedReadStatesStorage.UpsertAsync(chatId, userUuid, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10));

        var newerMessageId = Guid.NewGuid();
        var newerReadAt = DateTime.UtcNow;
        var applied = await _h.FederatedReadStatesStorage.UpsertAsync(chatId, userUuid, newerMessageId, newerReadAt);

        applied.Should().BeTrue();
        var stored = (await _h.FederatedReadStatesStorage.GetForChatAsync(chatId)).Single();
        stored.LastReadFederatedMessageId.Should().Be(newerMessageId);
        stored.ReadAt.Should().Be(newerReadAt);
    }

    [Fact]
    public async Task UpsertAsync_StaleRead_Ignored()
    {
        var chatId = Guid.NewGuid();
        var userUuid = Guid.NewGuid();
        var currentMessageId = Guid.NewGuid();
        var currentReadAt = DateTime.UtcNow;
        await _h.FederatedReadStatesStorage.UpsertAsync(chatId, userUuid, currentMessageId, currentReadAt);

        var applied = await _h.FederatedReadStatesStorage.UpsertAsync(
            chatId, userUuid, Guid.NewGuid(), currentReadAt.AddMinutes(-5));

        applied.Should().BeFalse();
        var stored = (await _h.FederatedReadStatesStorage.GetForChatAsync(chatId)).Single();
        stored.LastReadFederatedMessageId.Should().Be(currentMessageId);
        stored.ReadAt.Should().Be(currentReadAt);
    }
}
