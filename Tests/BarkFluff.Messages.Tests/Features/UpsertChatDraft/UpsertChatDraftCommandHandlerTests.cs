using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.UpsertChatDraft;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.UpsertChatDraft;

public class UpsertChatDraftCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private UpsertChatDraftCommandHandler CreateHandler(long userId) => new(
        _h.ChatsStorage,
        _h.ChatDraftsStorage,
        _h.MessagesStorage,
        _h.CreateUserContext(userId));

    [Fact]
    public async Task Handle_SavesReplyOnlyDraftForChatMember()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var reply = await _h.SeedMessage(chat.Id, 2);

        var response = await CreateHandler(1).Handle(new UpsertChatDraftCommand
        {
            ChatId = chat.Id,
            ReplyToMessageId = reply.Id
        }, CancellationToken.None);

        response.Draft.Text.Should().BeEmpty();
        response.Draft.ReplyToMessageId.Should().Be(reply.Id);
        Guid.TryParse(response.Draft.Revision, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RejectsReplyFromAnotherChat()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var otherChat = await _h.SeedChat(memberUserIds: [1, 3]);
        var reply = await _h.SeedMessage(otherChat.Id, 3);

        var act = () => CreateHandler(1).Handle(new UpsertChatDraftCommand
        {
            ChatId = chat.Id,
            Text = "Текст",
            ReplyToMessageId = reply.Id
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_RejectsPrivateChat()
    {
        var chat = await _h.SeedChat(type: ChatType.Private, memberUserIds: [1, 2]);

        var act = () => CreateHandler(1).Handle(new UpsertChatDraftCommand
        {
            ChatId = chat.Id,
            Text = "Текст"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotRegularException>();
    }
}
