using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.DeleteChat;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Queue.Messages;

namespace BarkFluff.Messages.Tests.Features.DeleteChat;

public class DeleteChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly MessageQueueSender _queueSender;

    public DeleteChatCommandHandlerTests()
    {
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private DeleteChatCommandHandler CreateHandler(long userId)
    {
        return new DeleteChatCommandHandler(
            _h.ChatsStorage,
            _h.MessagesStorage,
            _h.EncryptedMessagesStorage,
            _h.PinnedMessagesStorage,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<DeleteChatCommandHandler>());
    }

    [Fact]
    public async Task Handle_HideOnly_SetsHiddenAtOnlyForCaller()
    {
        var userId = 1L;
        var peerId = 2L;
        var chat = await _h.SeedChat(memberUserIds: [userId, peerId]);
        await _h.SeedMessage(chat.Id, userId, "hello");
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteChatCommand { ChatId = chat.Id, DeleteForEveryone = false }, CancellationToken.None);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.First(m => m.UserId == userId).HiddenAt.Should().NotBeNull();
        members.First(m => m.UserId == peerId).HiddenAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_HideOnly_DoesNotWipeMessageContent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "hello");
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteChatCommand { ChatId = chat.Id, DeleteForEveryone = false }, CancellationToken.None);

        var stored = await _h.MessagesStorage.GetMessageById(message.Id);
        stored!.IsDeleted.Should().BeFalse();
        stored.Content!.Text.Should().Be("hello");
    }

    [Fact]
    public async Task Handle_DeleteForEveryone_SetsHiddenAtForBothAndWipesMessages()
    {
        var userId = 1L;
        var peerId = 2L;
        var chat = await _h.SeedChat(memberUserIds: [userId, peerId]);
        var message = await _h.SeedMessage(chat.Id, userId, "secret text");
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteChatCommand { ChatId = chat.Id, DeleteForEveryone = true }, CancellationToken.None);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.Should().OnlyContain(m => m.HiddenAt != null);

        var stored = await _h.MessagesStorage.GetMessageById(message.Id);
        stored!.IsDeleted.Should().BeTrue();
        stored.Content!.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DeleteForEveryone_PrivateChat_WipesEncryptedMessages()
    {
        var userId = 1L;
        var peerId = 2L;
        var deviceId = Guid.NewGuid();
        var chat = await _h.SeedChat(type: ChatType.Private, memberUserIds: [userId, peerId],
            privateInviteState: PrivateChatInviteState.Accepted);
        var encrypted = await _h.SeedEncryptedMessage(chat.Id, userId, deviceId);
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteChatCommand { ChatId = chat.Id, DeleteForEveryone = true }, CancellationToken.None);

        var stored = await _h.EncryptedMessagesStorage.GetByIdAsync(encrypted.Id);
        stored!.IsDeleted.Should().BeTrue();
        stored.Ciphertext.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FederatedChat_DeleteForEveryoneIsClampedToHideOnly()
    {
        var userId = 1L;
        var chat = await _h.SeedFederatedChat(userId, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var message = await _h.SeedFederatedMessage(chat.Id, Guid.NewGuid(), Guid.NewGuid(), text: "fed text");
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteChatCommand { ChatId = chat.Id, DeleteForEveryone = true }, CancellationToken.None);

        var stored = await _h.MessagesStorage.GetMessageById(message.Id);
        stored!.IsDeleted.Should().BeFalse("нет протокола синхронного стирания копии на чужой ноде");

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.First(m => m.UserId == userId).HiddenAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_GroupChat_ThrowsChatTypeMismatchException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new DeleteChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatTypeMismatchException>();
    }

    [Fact]
    public async Task Handle_NoAccessToChat_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var handler = CreateHandler(99);

        var act = async () => await handler.Handle(new DeleteChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_PublishesChatHiddenEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteChatCommand { ChatId = chat.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<ChatHiddenEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
