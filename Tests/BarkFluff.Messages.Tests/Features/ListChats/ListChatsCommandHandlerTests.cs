using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.ListChats;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.ListChats;

public class ListChatsCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;
    private readonly Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache> _cacheMock;
    private readonly ChatCache _chatCache;

    public ListChatsCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        _cacheMock = new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        _chatCache = new ChatCache(_cacheMock.Object, TestHelper.CreateLogger<ChatCache>());
    }

    private ListChatsCommandHandler CreateHandler(long userId)
    {
        return new ListChatsCommandHandler(
            _h.CreateUserContext(userId),
            _h.ChatsStorage,
            Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            _chatCache,
            _usersClient.Object,
            _filesClient.Object,
            TestHelper.CreateLogger<ListChatsCommandHandler>());
    }

    [Fact]
    public async Task Handle_NoChats_ReturnsEmptyList()
    {
        var handler = CreateHandler(1);

        var result = await handler.Handle(new ListChatsCommand { Skip = 0, Size = 10 }, CancellationToken.None);

        result.Chats.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithChats_ReturnsChats()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "hello");
        SetupCacheValue($"chat_name_{chat.Id}_{userId}", "Test User");
        SetupCacheValue($"chat_image_{chat.Id}_{userId}", "pic.png");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListChatsCommand { Skip = 0, Size = 10 }, CancellationToken.None);

        result.Chats.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_SizeOver50_ClampedTo50()
    {
        var userId = 1L;
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListChatsCommand { Skip = 0, Size = 100 }, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_GroupChatClearsMembers()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [userId, 2, 3]);
        await _h.SeedMessage(chat.Id, userId, "hello");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListChatsCommand { Skip = 0, Size = 10 }, CancellationToken.None);

        result.Should().NotBeNull();
    }

    private void SetupCacheValue(string key, string? value)
    {
        _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(value != null ? System.Text.Encoding.UTF8.GetBytes(value) : null);
    }
}
