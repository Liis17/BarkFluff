using BarkFluff.Messages.Features.LeaveChat;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Queue.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.LeaveChat;

public class LeaveChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly MessageQueueSender _queueSender;

    public LeaveChatCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        SetupUsersClient();
    }

    private LeaveChatCommandHandler CreateHandler(long userId)
    {
        return new LeaveChatCommandHandler(
            _h.ChatsStorage,
            _h.MessagesStorage,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<LeaveChatCommandHandler>());
    }

    private void SetupUsersClient()
    {
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns<GetByIdRequest, Metadata, DateTime?, CancellationToken>((req, _, _, _) =>
                new AsyncUnaryCall<GetByIdResponse>(
                    Task.FromResult(new GetByIdResponse
                    {
                        User = new User { Id = req.UserId, FirstName = "Test", LastName = "User" }
                    }),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { }));
    }

    [Fact]
    public async Task Handle_ValidLeave_RemovesCallerFromChat_NotOthers()
    {
        var leaverId = 1L;
        var stayerId = 2L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [leaverId, stayerId, 3]);
        var handler = CreateHandler(leaverId);

        await handler.Handle(new LeaveChatCommand { ChatId = chat.Id }, CancellationToken.None);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.Should().NotContain(m => m.UserId == leaverId);
        members.Should().Contain(m => m.UserId == stayerId);
    }

    [Fact]
    public async Task Handle_ValidLeave_SelfKickNotBlockedByPermissions()
    {
        // В отличие от KickUser, обычный участник (не в UsersCanKick) может сам выйти из группы.
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [userId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, creatorId: 2, usersCanKick: [2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new LeaveChatCommand { ChatId = chat.Id }, CancellationToken.None);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.Should().NotContain(m => m.UserId == userId);
    }

    [Fact]
    public async Task Handle_ValidLeave_CreatesSystemMessage()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new LeaveChatCommand { ChatId = chat.Id }, CancellationToken.None);

        var messages = _h.DbContext.Messages.ToList();
        messages.Should().ContainSingle(m => m.Type == Domain.MessageContentType.System);
    }

    [Fact]
    public async Task Handle_ValidLeave_PublishesMessageToQueue()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new LeaveChatCommand { ChatId = chat.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotGroupChat_ThrowsChatTypeMismatchException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(isGroupChat: false, memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new LeaveChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatTypeMismatchException>();
    }

    [Fact]
    public async Task Handle_NoAccessToChat_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1, 2]);
        var handler = CreateHandler(99);

        var act = async () => await handler.Handle(new LeaveChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }
}
