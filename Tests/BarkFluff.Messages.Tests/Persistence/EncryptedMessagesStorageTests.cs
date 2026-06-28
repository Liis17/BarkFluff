using BarkFluff.Messages.Persistence.Services;

namespace BarkFluff.Messages.Tests.Persistence;

public class EncryptedMessagesStorageTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task AddAsync_SavesAndReturnsMessage()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);

        var result = await _h.EncryptedMessagesStorage.AddAsync(
            chat.Id, 1, Guid.NewGuid(), new byte[100], new byte[12], Array.Empty<byte>());

        result.Id.Should().BeGreaterThan(0);
        result.ChatId.Should().Be(chat.Id);
        result.SenderId.Should().Be(1);
        result.Ciphertext.Should().HaveCount(100);
    }

    [Fact]
    public async Task GetByIdAsync_Existing_ReturnsMessage()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid());

        var result = await _h.EncryptedMessagesStorage.GetByIdAsync(msg.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _h.EncryptedMessagesStorage.GetByIdAsync(99999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EditAsync_UpdatesCiphertextAndNonce()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid());
        var newCiphertext = new byte[200];
        var newNonce = new byte[24];
        var newAad = new byte[50];

        var result = await _h.EncryptedMessagesStorage.EditAsync(msg.Id, newCiphertext, newNonce, newAad);

        result.Ciphertext.Should().BeEquivalentTo(newCiphertext);
        result.Nonce.Should().BeEquivalentTo(newNonce);
        result.AssociatedData.Should().BeEquivalentTo(newAad);
        result.IsEdited.Should().BeTrue();
        result.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EditAsync_DeletedMessage_ThrowsInvalidOperationException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid(), isDeleted: true);

        var act = async () => await _h.EncryptedMessagesStorage.EditAsync(msg.Id, new byte[100], new byte[12], Array.Empty<byte>());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EditAsync_NotFound_ThrowsInvalidOperationException()
    {
        var act = async () => await _h.EncryptedMessagesStorage.EditAsync(99999, new byte[100], new byte[12], Array.Empty<byte>());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SoftDeleteAsync_MarksDeletedAndClearsCiphertext()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid());

        var result = await _h.EncryptedMessagesStorage.SoftDeleteAsync(msg.Id);

        result.Should().BeTrue();
        var dbMsg = await _h.EncryptedMessagesStorage.GetByIdAsync(msg.Id);
        dbMsg!.IsDeleted.Should().BeTrue();
        dbMsg.Ciphertext.Should().BeEmpty();
        dbMsg.Nonce.Should().BeEmpty();
        dbMsg.AssociatedData.Should().BeEmpty();
    }

    [Fact]
    public async Task SoftDeleteAsync_AlreadyDeleted_ReturnsFalse()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid(), isDeleted: true);

        var result = await _h.EncryptedMessagesStorage.SoftDeleteAsync(msg.Id);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteAsync_NotFound_ReturnsFalse()
    {
        var result = await _h.EncryptedMessagesStorage.SoftDeleteAsync(99999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListByChatAsync_NoFromMessage_ReturnsLatest()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        for (int i = 0; i < 5; i++)
            await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid());

        var result = await _h.EncryptedMessagesStorage.ListByChatAsync(chat.Id, null, 3, 0);

        result.Should().HaveCount(3);
    }
}
