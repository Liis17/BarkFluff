using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.MarkAsRead;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Messages.Tests.Features.MarkAsRead;

public class MarkAsReadCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly ReadByQueueSender _queueSender;

    public MarkAsReadCommandHandlerTests()
    {
        _queueSender = new ReadByQueueSender(_h.PublishEndpointMock.Object);
    }

    private MarkAsReadCommandHandler CreateHandler(long userId)
    {
        return new MarkAsReadCommandHandler(
            _h.MessagesStorage,
            _h.ChatsStorage,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<MarkAsReadCommandHandler>());
    }

    private MarkAsReadCommandHandler CreateOutboxHandler(long userId) => new(
        _h.MessagesStorage,
        _h.ChatsStorage,
        _h.CreateUserContext(userId),
        _queueSender,
        _h.Metrics,
        TestHelper.CreateLogger<MarkAsReadCommandHandler>(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messages:ChatReadRangeEnabled"] = "true",
        }).Build());

    private MarkChatAsReadUpToCommandHandler CreateRangeHandler(long userId) => new(
        _h.MessagesStorage,
        _h.ChatsStorage,
        _h.CreateUserContext(userId),
        _h.Metrics);

    [Fact]
    public async Task Handle_EmptyMessageIds_ReturnsWithoutError()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new MarkAsReadCommand { MessageIds = [] }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_NoAccessToChat_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var message = await _h.SeedMessage(chat.Id, 99, "test");
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_ValidMessages_ReturnsSuccessfully()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, 2, "msg1", readBy: [2]);
        var msg2 = await _h.SeedMessage(chat.Id, 2, "msg2", readBy: [2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [msg1.Id, msg2.Id] }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageReadEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_AlreadyReadMessage_DoesNotPublishReadEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, 2, "message", readBy: [2, userId]);
        var handler = CreateHandler(userId);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            publisher => publisher.Publish(
                It.IsAny<Shared.Queue.Messages.MessageReadEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NewRead_PublishesSnapshotAndNewReader()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, 2, "message", readBy: [2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            publisher => publisher.Publish(
                It.Is<Shared.Queue.Messages.MessageReadEvent>(@event =>
                    @event.NewReadBy.SequenceEqual(new[] { 2L, userId }) &&
                    @event.NewReaders.SequenceEqual(new[] { userId })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoMessagesFound_ReturnsWithoutError()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new MarkAsReadCommand { MessageIds = [99999] }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_FederatedChat_OnlyAnchorMessagePublishesUpToFederatedRead()
    {
        // Прочтение федерируется как "прочитано до X" (docs/rearch/05, «Read receipts»), не по
        // сообщению — среди нескольких прочитанных за раз в одном fed-чате событие с fed-полями
        // должно уйти только для одного (последнего) сообщения, не для каждого.
        var userId = 1L;
        var localUuid = Guid.NewGuid();
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(userId, localUuid, remoteUuid, "remote.test");
        var msg1 = await _h.SeedFederatedMessage(chat.Id, Guid.NewGuid(), senderUuid: remoteUuid, senderId: null, text: "m1");
        var msg2 = await _h.SeedFederatedMessage(chat.Id, Guid.NewGuid(), senderUuid: remoteUuid, senderId: null, text: "m2");
        var handler = CreateHandler(userId);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [msg1.Id, msg2.Id] }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(
                It.Is<Shared.Queue.Messages.MessageReadEvent>(e =>
                    e.MessageId == msg2.Id
                    && e.IsFederated
                    && e.ReaderUuid == localUuid
                    && e.UpToFederatedMessageId == msg2.FederatedId
                    && e.RemoteParticipants.Count == 1
                    && e.RemoteParticipants[0].Uuid == remoteUuid),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(
                It.Is<Shared.Queue.Messages.MessageReadEvent>(e => e.MessageId == msg1.Id && !e.IsFederated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithRangeFeatureEnabled_UsesTransactionalOutboxForExactIds()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var message = await _h.SeedMessage(chat.Id, 2, readBy: [2]);
        var handler = CreateOutboxHandler(1);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);
        await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);

        _h.DbContext.MessageOutbox.Should().ContainSingle(entry =>
            entry.MessageId == message.Id && entry.EventKind == MessageOutboxEventKind.ReadBy);
        _h.PublishEndpointMock.Verify(
            publisher => publisher.Publish(It.IsAny<Shared.Queue.Messages.MessageReadEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RangeHandlerMarksOnlyTheFirstHundredUnreadRowsIncludingSystemAndOwnMessages()
    {
        const long reader = 1;
        var chat = await _h.SeedChat(memberUserIds: [reader, 2]);
        var unread = new List<Message>();
        for (var index = 0; index < 102; index++)
        {
            var isOwnSystemMessage = index == 0;
            unread.Add(await _h.SeedMessage(
                chat.Id,
                isOwnSystemMessage ? reader : 2,
                $"message-{index}",
                isOwnSystemMessage ? MessageContentType.System : MessageContentType.Generic,
                readBy: isOwnSystemMessage ? [] : [2]));
        }
        var deletedAnchor = await _h.SeedMessage(chat.Id, 2, "deleted anchor", readBy: [2], isDeleted: true);
        var beyondCutoff = await _h.SeedMessage(chat.Id, 2, "newer", readBy: [2]);
        var handler = CreateRangeHandler(reader);

        var firstHasMore = await handler.Handle(
            new MarkChatAsReadUpToCommand(chat.Id, deletedAnchor.Id), CancellationToken.None);
        firstHasMore.Should().BeTrue();
        _h.DbContext.MessageOutbox.Should().HaveCount(100);

        var secondHasMore = await handler.Handle(
            new MarkChatAsReadUpToCommand(chat.Id, deletedAnchor.Id), CancellationToken.None);
        secondHasMore.Should().BeFalse();
        _h.DbContext.MessageOutbox.Should().HaveCount(102);

        var changed = _h.DbContext.Messages.Where(message => unread.Select(item => item.Id).Contains(message.Id)).ToList();
        changed.Should().OnlyContain(message => message.ReadBy.Contains(reader));
        changed.Single(message => message.Id == unread[0].Id).Type.Should().Be(MessageContentType.System);
        changed.Single(message => message.Id == unread[0].Id).SenderId.Should().Be(reader);
        (await _h.MessagesStorage.GetMessageById(deletedAnchor.Id))!.ReadBy.Should().Contain(2);
        (await _h.MessagesStorage.GetMessageById(beyondCutoff.Id))!.ReadBy.Should().NotContain(reader);
        _h.DbContext.MessageOutbox.Should().OnlyContain(entry => entry.EventKind == MessageOutboxEventKind.ReadBy);
    }

    [Fact]
    public async Task RangeHandlerRequiresPositiveCutoffAccessRegularChatAndChatAnchor()
    {
        var handler = CreateRangeHandler(1);
        var invalidCutoff = async () => await handler.Handle(
            new MarkChatAsReadUpToCommand(Guid.NewGuid(), 0), CancellationToken.None);
        await invalidCutoff.Should().ThrowAsync<MessageNotFoundException>();

        var privateChat = await _h.SeedChat(type: ChatType.Private, memberUserIds: [1, 2]);
        var privateAnchor = await _h.SeedMessage(privateChat.Id, 2);
        var privateRequest = async () => await handler.Handle(
            new MarkChatAsReadUpToCommand(privateChat.Id, privateAnchor.Id), CancellationToken.None);
        await privateRequest.Should().ThrowAsync<ChatTypeMismatchException>();

        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var other = await _h.SeedChat(memberUserIds: [1, 2]);
        var foreignAnchor = await _h.SeedMessage(other.Id, 2);
        var foreignAnchorRequest = async () => await handler.Handle(
            new MarkChatAsReadUpToCommand(chat.Id, foreignAnchor.Id), CancellationToken.None);
        await foreignAnchorRequest.Should().ThrowAsync<MessageNotFoundException>();

        var inaccessible = await _h.SeedChat(memberUserIds: [2, 3]);
        var inaccessibleAnchor = await _h.SeedMessage(inaccessible.Id, 2);
        var inaccessibleRequest = async () => await handler.Handle(
            new MarkChatAsReadUpToCommand(inaccessible.Id, inaccessibleAnchor.Id), CancellationToken.None);
        await inaccessibleRequest.Should().ThrowAsync<NoAccessToChatException>();
    }
}
