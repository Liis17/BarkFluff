using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.GetChatInfo;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.GetChatInfo;

public class GetChatInfoCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache> _cacheMock;
    private readonly ChatCache _chatCache;

    public GetChatInfoCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _cacheMock = new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        _chatCache = new ChatCache(_cacheMock.Object, TestHelper.CreateLogger<ChatCache>());
    }

    private GetChatInfoCommandHandler CreateHandler(long userId)
    {
        return new GetChatInfoCommandHandler(
            _h.ChatsStorage,
            _chatCache,
            _h.CreateUserContext(userId),
            _usersClient.Object,
            TestHelper.CreateLogger<GetChatInfoCommandHandler>());
    }

    [Fact]
    public async Task Handle_GroupChat_ReturnsInfo()
    {
        var chat = await _h.SeedChat(isGroupChat: true, title: "Test Group", memberUserIds: [1, 2]);
        await _h.SeedMessage(chat.Id, 1, "hello");
        var handler = CreateHandler(1);

        var result = await handler.Handle(new GetChatInfoCommand { ChatId = chat.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        result.IsGroupChat.Should().BeTrue();
        result.Title.Should().Be("Test Group");
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        await _h.SeedMessage(chat.Id, 99, "hello");
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new GetChatInfoCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_ChatNotFound_ThrowsNoAccessToChatException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new GetChatInfoCommand { ChatId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_PrivateChat_LoadsTitleFromCache()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        await _h.SeedMessage(chat.Id, 1, "hello");
        SetupCacheValue($"chat_name_{chat.Id}_1", "Cached Name");
        SetupCacheValue($"chat_image_{chat.Id}_1", "Cached Pic");
        var handler = CreateHandler(1);

        var result = await handler.Handle(new GetChatInfoCommand { ChatId = chat.Id }, CancellationToken.None);

        result.Title.Should().Be("Cached Name");
        result.Picture.Should().Be("Cached Pic");
    }

    [Fact]
    public async Task Handle_PrivateChatNoCache_LoadsFromUsersApi()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        await _h.SeedMessage(chat.Id, 1, "hello");
        SetupCacheValue($"chat_name_{chat.Id}_1", null);
        SetupUsersClient(2);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new GetChatInfoCommand { ChatId = chat.Id }, CancellationToken.None);

        result.Title.Should().Be("Test User");
    }

    private void SetupUsersClient(long userId)
    {
        var response = new GetByIdResponse
        {
            User = new User { Id = userId, FirstName = "Test", LastName = "User" }
        };
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private void SetupCacheValue(string key, string? value)
    {
        _cacheMock.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(value != null ? System.Text.Encoding.UTF8.GetBytes(value) : null);
    }
}
