using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.GetPersonChatId;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.GetPersonChatId;

public class GetPersonChatIdCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<ChatCache> _chatCache;

    public GetPersonChatIdCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        var cacheMock = new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        _chatCache = new Mock<ChatCache>(cacheMock.Object, TestHelper.CreateLogger<ChatCache>());
    }

    private GetPersonChatIdCommandHandler CreateHandler(long userId)
    {
        return new GetPersonChatIdCommandHandler(
            _h.ChatsStorage,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _chatCache.Object,
            _h.Metrics,
            TestHelper.CreateLogger<GetPersonChatIdCommandHandler>());
    }

    private void SetupUsersClient(long userId, string firstName = "Test", string lastName = "User")
    {
        var response = new GetByIdResponse
        {
            User = new User { Id = userId, FirstName = firstName, LastName = lastName }
        };
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    [Fact]
    public async Task Handle_ExistingChat_ReturnsChatId()
    {
        SetupUsersClient(2);
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new GetPersonChatIdCommand { UserId = 2 }, CancellationToken.None);

        result.ChatId.Should().Be(chat.Id.ToString());
    }

    [Fact]
    public async Task Handle_NoExistingChat_CreatesNewChat()
    {
        SetupUsersClient(2);
        SetupUsersClient(1);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new GetPersonChatIdCommand { UserId = 2 }, CancellationToken.None);

        result.ChatId.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.ChatId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SelfChat_ReturnsExistingChatId()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 1]);
        var handler = CreateHandler(1);

        SetupUsersClient(1);
        var result = await handler.Handle(new GetPersonChatIdCommand { UserId = 1 }, CancellationToken.None);

        result.ChatId.Should().Be(chat.Id.ToString());
    }

    [Fact]
    public async Task Handle_DuplicateRegularChats_ReturnsChatWithLatestMessage()
    {
        SetupUsersClient(2);
        var olderChat = await _h.SeedChat(memberUserIds: [1, 2]);
        await _h.SeedMessage(olderChat.Id, 1, "older", sentAt: DateTime.UtcNow.AddDays(-1));
        var newerChat = await _h.SeedChat(memberUserIds: [1, 2]);
        await _h.SeedMessage(newerChat.Id, 1, "newer", sentAt: DateTime.UtcNow);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new GetPersonChatIdCommand { UserId = 2 }, CancellationToken.None);

        result.ChatId.Should().Be(newerChat.Id.ToString());
    }

    [Fact]
    public async Task Handle_PrivateChatExists_CreatesRegularChat()
    {
        SetupUsersClient(2);
        SetupUsersClient(1);
        await _h.SeedChat(type: BarkFluff.Messages.Domain.ChatType.Private, memberUserIds: [1, 2]);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new GetPersonChatIdCommand { UserId = 2 }, CancellationToken.None);

        result.ChatId.Should().NotBeNullOrEmpty();
        result.ChatId.Should().NotBe(_h.DbContext.Chats.Single(c => c.Type == BarkFluff.Messages.Domain.ChatType.Private).Id.ToString());
        _h.DbContext.Chats.Count(c => c.Type == BarkFluff.Messages.Domain.ChatType.Regular).Should().Be(1);
    }
}
