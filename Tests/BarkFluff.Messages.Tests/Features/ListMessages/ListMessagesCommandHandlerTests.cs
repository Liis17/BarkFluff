using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.ListMessages;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.ListMessages;

public class ListMessagesCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;

    public ListMessagesCommandHandlerTests()
    {
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
    }

    private ListMessagesCommandHandler CreateHandler(long userId)
    {
        return new ListMessagesCommandHandler(
            _h.CreateUserContext(userId),
            _h.ChatsStorage,
            _h.MessagesStorage,
            _filesClient.Object,
            TestHelper.CreateLogger<ListMessagesCommandHandler>());
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new ListMessagesCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsMessages()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "msg1");
        await _h.SeedMessage(chat.Id, userId, "msg2");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListMessagesCommand
        {
            ChatId = chat.Id,
            Count = 50
        }, CancellationToken.None);

        result.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_DeletedMessagesExcluded()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "visible");
        await _h.SeedMessage(chat.Id, userId, "deleted", isDeleted: true);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListMessagesCommand
        {
            ChatId = chat.Id,
            Count = 50
        }, CancellationToken.None);

        result.Messages.Should().HaveCount(1);
        result.Messages[0].Content.Text.Should().Be("visible");
    }

    [Fact]
    public async Task Handle_FromMessageIdNotFound_ThrowsMessageNotFoundException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "msg");
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new ListMessagesCommand
        {
            ChatId = chat.Id,
            FromMessageId = 99999,
            Count = 50
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_BiDirectionalPagination_ReturnsMessages()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, userId, "old");
        var msg2 = await _h.SeedMessage(chat.Id, userId, "middle");
        var msg3 = await _h.SeedMessage(chat.Id, userId, "new");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListMessagesCommand
        {
            ChatId = chat.Id,
            FromMessageId = msg2.Id,
            OffsetBefore = 10,
            OffsetAfter = 10
        }, CancellationToken.None);

        result.Messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_ZeroCountDefault_ClampedTo50()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "msg");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListMessagesCommand
        {
            ChatId = chat.Id,
            Count = 0
        }, CancellationToken.None);

        result.Messages.Should().HaveCount(1);
    }
}
