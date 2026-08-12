using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

using OutgoingMsg = BarkFluff.Messages.Features.SendMessage.OutgoingMessage;

namespace BarkFluff.Messages.Tests.Features.SendMessage;

/// <summary>
/// Разделение reply и forward. До него оба действия шли одним полем
/// <c>forwarded_message_id</c> и хранились одинаково — снапшотом оригинала.
/// </summary>
public class SendMessageReplyForwardTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient = new();
    private readonly ChatCache _chatCache;
    private readonly MessageQueueSender _queueSender;

    public SendMessageReplyForwardTests()
    {
        var cacheMock = new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        _chatCache = new ChatCache(cacheMock.Object, TestHelper.CreateLogger<ChatCache>());
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);

        _usersClient.Setup(c => c.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new ListByIdsResponse
            {
                Users = { new User { Id = 2, FirstName = "Original", LastName = "Author" } }
            }));
    }

    private SendMessageCommandHandler CreateHandler(long userId)
    {
        return new SendMessageCommandHandler(
            _h.ChatsStorage,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _filesClient.Object,
            _chatCache,
            _h.MessagesStorage,
            _queueSender,
            TestHelper.CreateConfiguration(),
            _h.Metrics,
            _h.CreateReplyPreviewResolver(_usersClient.Object),
            TestHelper.CreateLogger<SendMessageCommandHandler>());
    }

    // ---------- Reply ----------

    [Fact]
    public async Task Reply_StoresReferenceInsteadOfSnapshot()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var original = await _h.SeedMessage(chat.Id, senderId: 2, text: "original text");

        var result = await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = "answer", ReplyToMessageId = original.Id }
        }, CancellationToken.None);

        // Ссылка, а не копия: вложения-снапшота у ответа нет вовсе.
        result.Message.ReplyTo.Should().NotBeNull();
        result.Message.ReplyTo.MessageId.Should().Be(original.Id);
        result.Message.ReplyTo.TextPreview.Should().Be("original text");
        result.Message.Content.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reply_ToMessageFromAnotherChat_ThrowsMessageNotFound()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var otherChat = await _h.SeedChat(memberUserIds: [1, 3]);
        var foreign = await _h.SeedMessage(otherChat.Id, senderId: 3);

        var act = async () => await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = "answer", ReplyToMessageId = foreign.Id }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Reply_ToDeletedMessage_ThrowsMessageNotFound()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var deleted = await _h.SeedMessage(chat.Id, senderId: 2, isDeleted: true);

        var act = async () => await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = "answer", ReplyToMessageId = deleted.Id }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Reply_WithoutTextOrFiles_ThrowsMessageNotContainContext()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var original = await _h.SeedMessage(chat.Id, senderId: 2);

        // Ответ содержимым не является — в отличие от пересылки.
        var act = async () => await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { ReplyToMessageId = original.Id }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotContainContextException>();
    }

    // ---------- Forward ----------

    [Fact]
    public async Task Forward_SeveralMessages_KeepsClientOrderAndRecordsOrigin()
    {
        var source = await _h.SeedChat(memberUserIds: [1, 2]);
        var target = await _h.SeedChat(memberUserIds: [1, 3]);
        var first = await _h.SeedMessage(source.Id, senderId: 2, text: "first");
        var second = await _h.SeedMessage(source.Id, senderId: 2, text: "second");

        // Порядок задаёт клиент, а не выдача БД — просим обратный.
        var result = await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = target.Id,
            Message = new OutgoingMsg { Text = string.Empty, ForwardedMessageIds = [second.Id, first.Id] }
        }, CancellationToken.None);

        var forwards = result.Message.Content.Attachments
            .Where(a => a.Type == Proto.Shared.MessageAttachmentType.ForwardedMessage)
            .ToList();

        forwards.Should().HaveCount(2);
        forwards[0].ForwardedMessage.Text.Should().Be("second");
        forwards[0].ForwardedMessage.Order.Should().Be(0);
        forwards[1].ForwardedMessage.Text.Should().Be("first");
        forwards[1].ForwardedMessage.Order.Should().Be(1);

        // Обогащённый снапшот: без источника пересылка не может показать «переслано из ...».
        forwards[0].ForwardedMessage.OriginalChatId.Should().Be(source.Id.ToString());
        forwards[0].ForwardedMessage.OriginalSenderId.Should().Be(2);
        forwards[0].ForwardedMessage.OriginalSentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Forward_OverLimit_ThrowsTooManyForwardedMessages()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var ids = new List<long>();
        for (var i = 0; i < 21; i++)
            ids.Add((await _h.SeedMessage(chat.Id, senderId: 2)).Id);

        var act = async () => await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { ForwardedMessageIds = ids }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<TooManyForwardedMessagesException>();
    }

    [Fact]
    public async Task Forward_FromChatUserIsNotMemberOf_ThrowsNoAccessToChat()
    {
        var target = await _h.SeedChat(memberUserIds: [1, 2]);
        var foreignChat = await _h.SeedChat(memberUserIds: [8, 9]);
        var foreign = await _h.SeedMessage(foreignChat.Id, senderId: 8);

        var act = async () => await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = target.Id,
            Message = new OutgoingMsg { ForwardedMessageIds = [foreign.Id] }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Forward_WithoutTextIsStillContent()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var original = await _h.SeedMessage(chat.Id, senderId: 2, text: "shared");

        var result = await CreateHandler(1).Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = string.Empty, ForwardedMessageIds = [original.Id] }
        }, CancellationToken.None);

        result.Message.Content.Attachments.Should().ContainSingle();
    }
}
