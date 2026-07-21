using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.ImportFederatedMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.ImportFederatedMessage;

public class ImportFederatedMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly MessageQueueSender _queueSender;

    public ImportFederatedMessageCommandHandlerTests()
    {
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private ImportFederatedMessageCommandHandler CreateHandler(string ownServer = "home.test")
    {
        return new ImportFederatedMessageCommandHandler(
            _h.DbContext,
            _h.MessagesStorage,
            _h.ChatsStorage,
            _queueSender,
            TestHelper.CreateConfiguration(ownServer),
            _h.Metrics,
            TestHelper.CreateLogger<ImportFederatedMessageCommandHandler>());
    }

    private static ImportFederatedMessageRequest BuildRequest(Guid chatId, Guid federatedId, Guid senderUuid, string text = "hello")
        => new()
        {
            ChatId = chatId.ToString(),
            FederatedMessageId = federatedId.ToString(),
            SenderUuid = senderUuid.ToString(),
            SenderUsername = "alice",
            SenderServerName = "remote.test",
            Text = text,
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

    [Fact]
    public async Task Handle_UnknownChat_ThrowsChatUnknownException()
    {
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ImportFederatedMessageCommand(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())), CancellationToken.None);

        await act.Should().ThrowAsync<ChatUnknownException>();
    }

    [Fact]
    public async Task Handle_RejectedChat_ThrowsFederatedChatNotActiveException()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test", FederatedStatus.Rejected);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ImportFederatedMessageCommand(
            BuildRequest(chat.Id, Guid.NewGuid(), remoteUuid)), CancellationToken.None);

        await act.Should().ThrowAsync<FederatedChatNotActiveException>();
    }

    [Fact]
    public async Task Handle_ValidImport_PublishesNewMessageEventWithLastChangeAt()
    {
        // Баг #8: LastChangeAt обязан прокидываться в опубликованный NewMessageEvent, иначе
        // NewMessageFederationConsumer падает на wall-clock время обработки вместо origin_ts.
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var handler = CreateHandler();

        await handler.Handle(new ImportFederatedMessageCommand(
            BuildRequest(chat.Id, Guid.NewGuid(), remoteUuid)), CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.NewMessageEvent>(e => e.LastChangeAt.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateFederatedId_ReturnsExistingMessageId()
    {
        // Регрессия: (3) идемпотентность по (ChatId, FederatedId) — повторная доставка того же
        // NewMessage возвращает уже созданное сообщение вместо повторной вставки.
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        var handler = CreateHandler();

        var first = await handler.Handle(new ImportFederatedMessageCommand(
            BuildRequest(chat.Id, federatedId, remoteUuid)), CancellationToken.None);
        var second = await handler.Handle(new ImportFederatedMessageCommand(
            BuildRequest(chat.Id, federatedId, remoteUuid)), CancellationToken.None);

        second.MessageId.Should().Be(first.MessageId);
        _h.DbContext.Messages.Count(m => m.ChatId == chat.Id && m.FederatedId == federatedId).Should().Be(1);
    }
}
