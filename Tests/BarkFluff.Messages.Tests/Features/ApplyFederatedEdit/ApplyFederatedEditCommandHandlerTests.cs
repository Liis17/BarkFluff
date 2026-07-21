using BarkFluff.Messages.Features.ApplyFederatedEdit;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.ApplyFederatedEdit;

public class ApplyFederatedEditCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly MessageQueueSender _queueSender;

    public ApplyFederatedEditCommandHandlerTests()
    {
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private ApplyFederatedEditCommandHandler CreateHandler(string ownServer = "home.test")
    {
        return new ApplyFederatedEditCommandHandler(
            _h.DbContext,
            _h.MessagesStorage,
            _h.ChatsStorage,
            _queueSender,
            TestHelper.CreateConfiguration(ownServer),
            _h.Metrics,
            TestHelper.CreateLogger<ApplyFederatedEditCommandHandler>());
    }

    private static ApplyFederatedEditRequest BuildRequest(
        Guid chatId, Guid federatedId, string newText, string originServer, DateTime originTs)
        => new()
        {
            ChatId = chatId.ToString(),
            FederatedMessageId = federatedId.ToString(),
            NewText = newText,
            OriginServer = originServer,
            OriginTsMs = new DateTimeOffset(originTs, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            EventId = Guid.NewGuid().ToString(),
        };

    [Fact]
    public async Task Handle_UnknownChat_ThrowsChatUnknownException()
    {
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedEditCommand(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), "x", "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ChatUnknownException>();
    }

    [Fact]
    public async Task Handle_UnknownMessage_ThrowsFederatedMessageUnknownException()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedEditCommand(
            BuildRequest(chat.Id, Guid.NewGuid(), "x", "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedMessageUnknownException>();
    }

    [Fact]
    public async Task Handle_RemoteEditsLocalAuthorMessage_ThrowsFederatedOriginMismatchException()
    {
        // Вектор (а), P2-02: чужая нода правит сообщение ЛОКАЛЬНОГО автора в нашей копии.
        var localUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, localUuid, Guid.NewGuid(), "remote.test");
        var federatedId = Guid.NewGuid();
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: localUuid, senderId: 1);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedEditCommand(
            BuildRequest(chat.Id, federatedId, "hacked", "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedOriginMismatchException>();
    }

    [Fact]
    public async Task Handle_WrongOriginEditsOtherPeerMessage_ThrowsFederatedOriginMismatchException()
    {
        // Вектор (б), P2-02: партнёрская нода правит сообщение НЕ своего пользователя.
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, senderId: null);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedEditCommand(
            BuildRequest(chat.Id, federatedId, "hacked", "attacker.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedOriginMismatchException>();
    }

    [Fact]
    public async Task Handle_ValidEdit_AppliesAndPublishesEditedEvent()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, text: "original",
            lastChangeAt: DateTime.UtcNow.AddMinutes(-5));
        var handler = CreateHandler();

        var response = await handler.Handle(new ApplyFederatedEditCommand(
            BuildRequest(chat.Id, federatedId, "edited", "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        response.Applied.Should().BeTrue();
        var updated = await _h.MessagesStorage.GetByFederatedIdAsync(chat.Id, federatedId);
        updated!.Content!.Text.Should().Be("edited");
        updated.IsEdited.Should().BeTrue();

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageEditedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_StaleEdit_IgnoredWithoutError()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        var currentTs = DateTime.UtcNow;
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, text: "current", lastChangeAt: currentTs);
        var handler = CreateHandler();

        var response = await handler.Handle(new ApplyFederatedEditCommand(
            BuildRequest(chat.Id, federatedId, "stale", "remote.test", currentTs.AddMinutes(-5))),
            CancellationToken.None);

        response.Applied.Should().BeFalse();
        var updated = await _h.MessagesStorage.GetByFederatedIdAsync(chat.Id, federatedId);
        updated!.Content!.Text.Should().Be("current");
    }

    [Fact]
    public async Task Handle_EditOnDeletedMessage_TerminallyIgnored()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, text: "deleted-content",
            lastChangeAt: DateTime.UtcNow.AddMinutes(-10), isDeleted: true);
        var handler = CreateHandler();

        // Метка правки новее удаления — но удаление терминально, правка всё равно игнорируется.
        var response = await handler.Handle(new ApplyFederatedEditCommand(
            BuildRequest(chat.Id, federatedId, "resurrected", "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        response.Applied.Should().BeFalse();
        var updated = await _h.MessagesStorage.GetByFederatedIdAsync(chat.Id, federatedId);
        updated!.IsDeleted.Should().BeTrue();
        updated.Content!.Text.Should().Be("deleted-content");
    }
}
