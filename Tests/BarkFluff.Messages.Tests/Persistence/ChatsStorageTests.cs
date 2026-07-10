using BarkFluff.Messages.Persistence.Services;

namespace BarkFluff.Messages.Tests.Persistence;

public class ChatsStorageTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task CreatePersonChat_CreatesChatWithTwoMembers()
    {
        var chat = await _h.ChatsStorage.CreatePersonChat(1, 2);

        chat.Should().NotBeNull();
        chat.Id.Should().NotBe(Guid.Empty);
        chat.IsGroupChat.Should().BeFalse();
        chat.Members.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreatePersonChat_SelfChat_CreatesChatWithSameUserTwice()
    {
        var chat = await _h.ChatsStorage.CreatePersonChat(1, 1);

        chat.Members.Should().HaveCount(2);
        chat.Members!.All(m => m.UserId == 1).Should().BeTrue();
    }

    [Fact]
    public async Task CreateGroupChat_CreatesGroupWithTitle()
    {
        var chat = await _h.ChatsStorage.CreateGroupChat([1, 2, 3], "Test Group", null);

        chat.IsGroupChat.Should().BeTrue();
        chat.Title.Should().Be("Test Group");
        chat.Members.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreatePrivateChat_CreatesPrivateChatWithSaltAndVerifier()
    {
        var salt = new byte[32];
        var verifier = new byte[32];
        var creation = await _h.ChatsStorage.CreatePrivateChat(1, 2, salt, verifier);
        var chat = creation.Chat;

        chat.Type.Should().Be(Domain.ChatType.Private);
        chat.KdfSalt.Should().BeEquivalentTo(salt);
        chat.PassphraseVerifier.Should().BeEquivalentTo(verifier);
        chat.Members.Should().HaveCount(1);
    }

    [Fact]
    public async Task CheckAccessToChat_Member_ReturnsTrue()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        var hasAccess = await _h.ChatsStorage.CheckAccessToChat(chat.Id, 1);

        hasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccessToChat_NonMember_ReturnsFalse()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        var hasAccess = await _h.ChatsStorage.CheckAccessToChat(chat.Id, 99);

        hasAccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserChatIdWithPerson_ExistingChat_ReturnsChatId()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        var chatId = await _h.ChatsStorage.GetUserChatIdWithPerson(2, 1);

        chatId.Should().Be(chat.Id);
    }

    [Fact]
    public async Task GetUserChatIdWithPerson_NoChat_ReturnsNull()
    {
        var chatId = await _h.ChatsStorage.GetUserChatIdWithPerson(2, 1);

        chatId.Should().BeNull();
    }

    [Fact]
    public async Task GetUserChatIdWithPerson_SelfChat_ReturnsChatId()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 1]);

        var chatId = await _h.ChatsStorage.GetUserChatIdWithPerson(1, 1);

        chatId.Should().Be(chat.Id);
    }

    [Fact]
    public async Task GetChatMembers_ReturnsPaginatedMembers()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2, 3, 4, 5]);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, 3);

        members.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetTotalChatMembers_ReturnsCorrectCount()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2, 3]);

        var total = await _h.ChatsStorage.GetTotalChatMembers(chat.Id);

        total.Should().Be(3);
    }

    [Fact]
    public async Task AddChatMember_AddsMember()
    {
        var chat = await _h.SeedChat(memberUserIds: [1]);
        var initialCount = await _h.ChatsStorage.GetTotalChatMembers(chat.Id);

        await _h.ChatsStorage.AddChatMember(chat.Id, 2);

        var newCount = await _h.ChatsStorage.GetTotalChatMembers(chat.Id);
        newCount.Should().Be(initialCount + 1);
    }

    [Fact]
    public async Task RemoveChatMember_RemovesMember()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2, 3]);

        await _h.ChatsStorage.RemoveChatMember(chat.Id, 2);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.Should().NotContain(m => m.UserId == 2);
    }

    [Fact]
    public async Task RemoveChatMember_NonExistent_DoesNothing()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        var act = async () => await _h.ChatsStorage.RemoveChatMember(chat.Id, 99);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteChat_RemovesChat()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        await _h.ChatsStorage.DeleteChat(chat.Id);

        var found = await _h.ChatsStorage.GetChat(chat.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task DeleteChat_NonExistent_DoesNothing()
    {
        var act = async () => await _h.ChatsStorage.DeleteChat(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetChat_ReturnsChatWithMembers()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        var result = await _h.ChatsStorage.GetChat(chat.Id);

        result.Should().NotBeNull();
        result!.Members.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetChat_NonExistent_ReturnsNull()
    {
        var result = await _h.ChatsStorage.GetChat(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateGroupChatInfo_SavesInfo()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1]);
        var info = new Domain.GroupChatInfo
        {
            ChatId = chat.Id,
            Creator = 1,
            UsersCanKick = [1],
            CreatedAt = DateTime.UtcNow
        };

        await _h.ChatsStorage.CreateGroupChatInfo(info);

        var saved = await _h.ChatsStorage.GetGroupChatInfo(chat.Id);
        saved.Should().NotBeNull();
        saved!.Creator.Should().Be(1);
    }
}
