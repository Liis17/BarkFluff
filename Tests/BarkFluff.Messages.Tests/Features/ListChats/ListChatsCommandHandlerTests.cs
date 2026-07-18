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
        _usersClient
            .Setup(client => client.GetMutedChatIdsAsync(
                It.IsAny<GetMutedChatIdsRequest>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(new GetMutedChatIdsResponse()));
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
        var command = new ListChatsCommand { Skip = 0, Size = 100 };

        await handler.Handle(command, CancellationToken.None);

        command.Size.Should().Be(50);
    }

    [Fact]
    public async Task Handle_GroupChatClearsMembers()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [userId, 2, 3]);
        await _h.SeedMessage(chat.Id, userId, "hello");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListChatsCommand { Skip = 0, Size = 10 }, CancellationToken.None);

        result.Chats.Should().ContainSingle();
        result.Chats[0].Members.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyChatsBeforeNonEmptyChats_DoNotConsumePage()
    {
        var userId = 1L;
        for (var i = 0; i < 60; i++)
        {
            await _h.SeedChat(isGroupChat: true, title: $"Empty {i}", memberUserIds: [userId, 2]);
        }

        var chat = await _h.SeedChat(isGroupChat: true, title: "Non Empty", memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "hello");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListChatsCommand { Skip = 0, Size = 10 }, CancellationToken.None);

        result.Chats.Should().ContainSingle();
        result.Chats[0].Id.Should().Be(chat.Id.ToString());
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_PageIsSortedByLastMessageBeforePagination()
    {
        var userId = 1L;
        var olderChat = await _h.SeedChat(isGroupChat: true, title: "Older", memberUserIds: [userId, 2]);
        await _h.SeedMessage(olderChat.Id, userId, "older", sentAt: DateTime.UtcNow.AddDays(-1));
        var newerChat = await _h.SeedChat(isGroupChat: true, title: "Newer", memberUserIds: [userId, 2]);
        await _h.SeedMessage(newerChat.Id, userId, "newer", sentAt: DateTime.UtcNow);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListChatsCommand { Skip = 0, Size = 1 }, CancellationToken.None);

        result.Chats.Should().ContainSingle();
        result.Chats[0].Id.Should().Be(newerChat.Id.ToString());
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_MuteServiceUnavailable_ReturnsChatsAsUnmuted()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "hello");
        _usersClient
            .Setup(client => client.GetMutedChatIdsAsync(
                It.IsAny<GetMutedChatIdsRequest>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Error connecting to subchannel")));
        var handler = CreateHandler(userId);

        var result = await handler.Handle(
            new ListChatsCommand { Skip = 0, Size = 10 },
            CancellationToken.None);

        result.Chats.Should().ContainSingle();
        result.Chats[0].Muted.Should().BeFalse();
    }

    private void SetupCacheValue(string key, string? value)
    {
        _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(value != null ? System.Text.Encoding.UTF8.GetBytes(value) : null);
    }

    private static AsyncUnaryCall<TResponse> CreateAsyncCall<TResponse>(TResponse response)
    {
        return new AsyncUnaryCall<TResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }
}
