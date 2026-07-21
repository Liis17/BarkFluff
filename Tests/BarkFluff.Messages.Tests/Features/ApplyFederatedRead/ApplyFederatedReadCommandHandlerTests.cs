using BarkFluff.Messages.Features.ApplyFederatedRead;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.ApplyFederatedRead;

public class ApplyFederatedReadCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly ReadByQueueSender _readByQueueSender;
    private readonly FederatedReadStatesStorage _federatedReadStatesStorage;

    public ApplyFederatedReadCommandHandlerTests()
    {
        _readByQueueSender = new ReadByQueueSender(_h.PublishEndpointMock.Object);
        _federatedReadStatesStorage = new FederatedReadStatesStorage(_h.DbContext);
    }

    private ApplyFederatedReadCommandHandler CreateHandler(string ownServer = "home.test")
    {
        return new ApplyFederatedReadCommandHandler(
            _h.MessagesStorage,
            _h.ChatsStorage,
            _federatedReadStatesStorage,
            _readByQueueSender,
            TestHelper.CreateConfiguration(ownServer),
            _h.Metrics,
            TestHelper.CreateLogger<ApplyFederatedReadCommandHandler>());
    }

    private static ApplyFederatedReadRequest BuildRequest(
        Guid chatId, Guid readerUuid, Guid? upTo, string originServer, DateTime originTs)
        => new()
        {
            ChatId = chatId.ToString(),
            ReaderUuid = readerUuid.ToString(),
            UpToFederatedMessageId = upTo?.ToString() ?? string.Empty,
            OriginServer = originServer,
            OriginTsMs = new DateTimeOffset(originTs, TimeSpan.Zero).ToUnixTimeMilliseconds(),
        };

    [Fact]
    public async Task Handle_UnknownChat_ThrowsChatUnknownException()
    {
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedReadCommand(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), null, "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ChatUnknownException>();
    }

    [Fact]
    public async Task Handle_ReaderNotRemoteMemberOfChat_ThrowsFederatedOriginMismatchException()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedReadCommand(
            BuildRequest(chat.Id, Guid.NewGuid(), null, "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedOriginMismatchException>();
    }

    [Fact]
    public async Task Handle_OriginNotReaderHomeServer_ThrowsFederatedOriginMismatchException()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ApplyFederatedReadCommand(
            BuildRequest(chat.Id, remoteUuid, null, "attacker.test", DateTime.UtcNow)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FederatedOriginMismatchException>();
    }

    [Fact]
    public async Task Handle_ValidRead_WithResolvableMessage_UpsertsAndPublishesEvent()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: remoteUuid, senderId: null);
        var handler = CreateHandler();

        var response = await handler.Handle(new ApplyFederatedReadCommand(
            BuildRequest(chat.Id, remoteUuid, federatedId, "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        response.Applied.Should().BeTrue();
        var states = await _federatedReadStatesStorage.GetForChatAsync(chat.Id);
        states.Should().ContainSingle(s => s.UserUuid == remoteUuid && s.LastReadFederatedMessageId == federatedId);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageReadEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRead_MessageNotYetImported_StoresStateWithoutPublishing()
    {
        // "up_to" указывает на ещё не догнанное catch-up'ом сообщение (2.6) — прочтение сохраняется,
        // но локальной рассылки пока нет (нечего показывать).
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var handler = CreateHandler();
        var unresolvedFederatedId = Guid.NewGuid();

        var response = await handler.Handle(new ApplyFederatedReadCommand(
            BuildRequest(chat.Id, remoteUuid, unresolvedFederatedId, "remote.test", DateTime.UtcNow)),
            CancellationToken.None);

        response.Applied.Should().BeTrue();
        var states = await _federatedReadStatesStorage.GetForChatAsync(chat.Id);
        states.Should().ContainSingle(s => s.LastReadFederatedMessageId == unresolvedFederatedId);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageReadEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_StaleRead_IgnoredAppliedFalse()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var handler = CreateHandler();
        var currentTs = DateTime.UtcNow;

        await handler.Handle(new ApplyFederatedReadCommand(
            BuildRequest(chat.Id, remoteUuid, Guid.NewGuid(), "remote.test", currentTs)),
            CancellationToken.None);

        var staleResponse = await handler.Handle(new ApplyFederatedReadCommand(
            BuildRequest(chat.Id, remoteUuid, Guid.NewGuid(), "remote.test", currentTs.AddMinutes(-5))),
            CancellationToken.None);

        staleResponse.Applied.Should().BeFalse();
    }
}
