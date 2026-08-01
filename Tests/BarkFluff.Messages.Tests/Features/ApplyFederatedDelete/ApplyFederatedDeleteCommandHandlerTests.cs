using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.ApplyFederatedDelete;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.ApplyFederatedDelete;

public class ApplyFederatedDeleteCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly MessageQueueSender _queueSender;
    private readonly PinnedMessagesStorage _pinnedMessagesStorage;

    public ApplyFederatedDeleteCommandHandlerTests()
    {
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        _pinnedMessagesStorage = new PinnedMessagesStorage(_h.DbContext);
    }

    private ApplyFederatedDeleteCommandHandler CreateHandler(string ownServer = "home.test")
    {
        return new ApplyFederatedDeleteCommandHandler(
            _h.DbContext,
            _h.MessagesStorage,
            _h.ChatsStorage,
            _pinnedMessagesStorage,
            _queueSender,
            TestHelper.CreateConfiguration(ownServer),
            _h.Metrics,
            TestHelper.CreateLogger<ApplyFederatedDeleteCommandHandler>());
    }

    private static ApplyFederatedDeleteRequest BuildRequest(Guid chatId, Guid federatedId, string originServer, DateTime originTs)
        => new()
        {
            ChatId = chatId.ToString(),
            FederatedMessageId = federatedId.ToString(),
            OriginServer = originServer,
            OriginTsMs = new DateTimeOffset(originTs, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            EventId = Guid.NewGuid().ToString(),
        };

    [Fact]
    public async Task Handle_UnknownChat_ThrowsChatUnknownException()
    {
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ChatUnknownException>();
    }

    [Fact]
    public async Task Handle_RejectedChat_ThrowsFederatedChatNotActiveException()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test", FederatedStatus.Rejected);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(chat.Id, Guid.NewGuid(), "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedChatNotActiveException>();
    }

    [Fact]
    public async Task Handle_MergedChat_ThrowsFederatedChatNotActiveException()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test", FederatedStatus.Merged);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(chat.Id, Guid.NewGuid(), "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedChatNotActiveException>();
    }

    [Fact]
    public async Task Handle_UnknownMessage_ThrowsFederatedMessageUnknownException()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(chat.Id, Guid.NewGuid(), "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedMessageUnknownException>();
    }

    [Fact]
    public async Task Handle_RemoteDeletesLocalAuthorMessage_ThrowsFederatedOriginMismatchException()
    {
        var localUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, localUuid, Guid.NewGuid(), "remote.test");
        var federatedId = Guid.NewGuid();
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: localUuid, senderId: 1);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(chat.Id, federatedId, "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedOriginMismatchException>();
    }

    [Fact]
    public async Task Handle_ValidDelete_AppliesAndPublishesDeletedEvent()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        var message = await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, text: "to-delete",
            lastChangeAt: DateTime.UtcNow.AddMinutes(-5));
        message.Content!.Attachments!.Add(new MessageAttachment
        {
            Type = MessageAttachmentType.Document,
            FileId = Guid.NewGuid().ToString(),
        });
        await _h.DbContext.SaveChangesAsync();
        var handler = CreateHandler();

        var response = await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(chat.Id, federatedId, "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        response.Applied.Should().BeTrue();
        _h.DbContext.ChangeTracker.Clear();
        var updated = await _h.MessagesStorage.GetByFederatedIdAsync(chat.Id, federatedId);
        updated!.IsDeleted.Should().BeTrue();
        updated.Content!.Text.Should().BeEmpty();
        updated.Content.Attachments.Should().BeEmpty();

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageDeletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_StaleDeleteAfterNewerEdit_Ignored()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        var currentTs = DateTime.UtcNow;
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, text: "edited-newer", lastChangeAt: currentTs);
        var handler = CreateHandler();

        // Удаление со старой меткой, пришедшее после уже применённой более новой правки — устарело.
        var response = await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(chat.Id, federatedId, "remote.test", currentTs.AddMinutes(-5))),
            CancellationToken.None);

        response.Applied.Should().BeFalse();
        var updated = await _h.MessagesStorage.GetByFederatedIdAsync(chat.Id, federatedId);
        updated!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DuplicateDelete_NoOpAppliedFalse()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, isDeleted: true,
            lastChangeAt: DateTime.UtcNow.AddMinutes(-10));
        var handler = CreateHandler();

        var response = await handler.Handle(new ApplyFederatedDeleteCommand(
            BuildRequest(chat.Id, federatedId, "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        response.Applied.Should().BeFalse();
    }
}
