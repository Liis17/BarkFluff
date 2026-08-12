using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.ImportFederatedMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Microsoft.EntityFrameworkCore;

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

    // ---- Снапшот вложений (этап 3.1) ----

    [Fact]
    public async Task Handle_WithAttachments_PersistsSnapshot()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var fileId = Guid.NewGuid().ToString();

        var request = BuildRequest(chat.Id, Guid.NewGuid(), remoteUuid);
        request.Attachments.Add(new FederatedFileRefFlat
        {
            OriginServer = "remote.test",
            FileId = fileId,
            Filename = "report.pdf",
            SizeBytes = 4096,
            AttachmentType = (int)MessageAttachmentType.Document,
        });

        await CreateHandler().Handle(new ImportFederatedMessageCommand(request), CancellationToken.None);

        // Attachments — owned-сущность: проецировать её отдельно от владельца EF не даёт,
        // поэтому забираем сообщения целиком.
        var stored = _h.DbContext.Messages
            .AsNoTracking()
            .AsEnumerable()
            .SelectMany(m => m.Content?.Attachments ?? [])
            .ToList();

        var attachment = stored.Should().ContainSingle().Subject;
        attachment.FileId.Should().Be(fileId);
        attachment.OriginServer.Should().Be("remote.test");
        attachment.FileName.Should().Be("report.pdf");
        attachment.FileSize.Should().Be(4096);
        attachment.Type.Should().Be(MessageAttachmentType.Document);
    }

    [Fact]
    public async Task Handle_MalformedAttachment_IsRejectedPermanently()
    {
        // Снапшот с чужой ноды не проходит валидацию → permanent REJECTED, не RETRY:
        // повторная доставка того же битого события ничего не исправит.
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");

        var request = BuildRequest(chat.Id, Guid.NewGuid(), remoteUuid);
        request.Attachments.Add(new FederatedFileRefFlat
        {
            OriginServer = "remote.test",
            FileId = "not-a-guid",
            SizeBytes = 1,
            AttachmentType = (int)MessageAttachmentType.Document,
        });

        var act = async () => await CreateHandler().Handle(
            new ImportFederatedMessageCommand(request), CancellationToken.None);

        await act.Should().ThrowAsync<FederatedAttachmentInvalidException>();
    }

    [Fact]
    public async Task Handle_ReplyToImportedMessage_ResolvesUuidToLocalId()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");
        var handler = CreateHandler();

        var originalFederatedId = Guid.NewGuid();
        var original = await handler.Handle(new ImportFederatedMessageCommand(
            BuildRequest(chat.Id, originalFederatedId, remoteUuid, "original")), CancellationToken.None);

        var replyRequest = BuildRequest(chat.Id, Guid.NewGuid(), remoteUuid, "answer");
        replyRequest.ReplyToFederatedMessageId = originalFederatedId.ToString();

        var reply = await handler.Handle(
            new ImportFederatedMessageCommand(replyRequest), CancellationToken.None);

        var stored = await _h.DbContext.Messages.FirstAsync(m => m.Id == reply.MessageId);
        stored.ReplyToMessageId.Should().Be(original.MessageId);
    }

    [Fact]
    public async Task Handle_ReplyToNotYetImportedMessage_StoresWithoutQuote()
    {
        // Дыра в истории — забота catch-up. Цитата не должна задерживать доставку самого
        // сообщения бесконечным RETRY.
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");

        var request = BuildRequest(chat.Id, Guid.NewGuid(), remoteUuid, "answer");
        request.ReplyToFederatedMessageId = Guid.NewGuid().ToString();

        var result = await CreateHandler().Handle(
            new ImportFederatedMessageCommand(request), CancellationToken.None);

        var stored = await _h.DbContext.Messages.FirstAsync(m => m.Id == result.MessageId);
        stored.ReplyToMessageId.Should().BeNull();
        stored.Content!.Text.Should().Be("answer");
    }

    [Fact]
    public async Task Handle_ForwardSnapshot_IsStoredAsForwardedAttachment()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");

        var request = BuildRequest(chat.Id, Guid.NewGuid(), remoteUuid, string.Empty);
        request.Forwards.Add(new FederatedForwardFlat
        {
            AuthorName = "Remote Author",
            Text = "forwarded body",
            Order = 0,
        });

        var result = await CreateHandler().Handle(
            new ImportFederatedMessageCommand(request), CancellationToken.None);

        var stored = await _h.DbContext.Messages
            .Include(m => m.Content!.Attachments)
            .FirstAsync(m => m.Id == result.MessageId);

        stored.Content!.Attachments.Should().ContainSingle(a =>
            a.Type == MessageAttachmentType.ForwardedMessage && a.ForwardedText == "forwarded body");
    }
}
