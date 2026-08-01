using BarkFluff.Messages.Persistence.Services;

namespace BarkFluff.Messages.Tests.Persistence;

public class ChatDraftsStorageTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Upsert_GetAndConditionalDelete_KeepOnlyExpectedRevision()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var storage = new ChatDraftsStorage(_h.DbContext);

        var first = await storage.UpsertAsync(chat.Id, 1, "Первый текст", 10);
        var firstRevision = first.Revision;
        var second = await storage.UpsertAsync(chat.Id, 1, "Второй текст", null);
        var secondRevision = second.Revision;

        (await storage.DeleteIfRevisionMatchesAsync(chat.Id, 1, firstRevision)).Should().BeFalse();
        var draft = await storage.GetAsync(chat.Id, 1);
        draft.Should().NotBeNull();
        draft!.Text.Should().Be("Второй текст");
        draft.ReplyToMessageId.Should().BeNull();

        (await storage.DeleteIfRevisionMatchesAsync(chat.Id, 1, secondRevision)).Should().BeTrue();
        (await storage.GetAsync(chat.Id, 1)).Should().BeNull();
    }

    [Fact]
    public async Task GetDraftChatIds_IsolatedByUser()
    {
        var firstChat = await _h.SeedChat(memberUserIds: [1, 2]);
        var secondChat = await _h.SeedChat(memberUserIds: [1, 3]);
        var storage = new ChatDraftsStorage(_h.DbContext);

        await storage.UpsertAsync(firstChat.Id, 1, "Для первого", null);
        await storage.UpsertAsync(secondChat.Id, 2, "Для второго", null);

        var ids = await storage.GetDraftChatIdsAsync(1, [firstChat.Id, secondChat.Id]);

        ids.Should().ContainSingle().Which.Should().Be(firstChat.Id);
    }
}
