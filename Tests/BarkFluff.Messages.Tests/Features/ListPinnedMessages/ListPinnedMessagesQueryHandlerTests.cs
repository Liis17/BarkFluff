using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.ListPinnedMessages;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.ListPinnedMessages;

public class ListPinnedMessagesQueryHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;

    public ListPinnedMessagesQueryHandlerTests()
    {
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
    }

    private ListPinnedMessagesQueryHandler CreateHandler(long userId)
    {
        return new ListPinnedMessagesQueryHandler(
            _h.PinnedMessagesStorage,
            _h.MessagesStorage,
            _h.ChatsStorage,
            _filesClient.Object,
            _h.CreateUserContext(userId),
            TestHelper.CreateLogger<ListPinnedMessagesQueryHandler>());
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new ListPinnedMessagesQuery { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_NoPinnedMessages_ReturnsEmptyList()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListPinnedMessagesQuery { ChatId = chat.Id }, CancellationToken.None);

        result.Pinned.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithPinnedMessages_ReturnsMappedResults()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var msg = await _h.SeedMessage(chat.Id, userId, "pinned");
        await _h.SeedPinnedMessage(chat.Id, msg.Id, userId);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListPinnedMessagesQuery { ChatId = chat.Id }, CancellationToken.None);

        result.Pinned.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
    }
}
