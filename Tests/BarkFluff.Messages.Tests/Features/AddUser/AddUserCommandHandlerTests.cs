using BarkFluff.Messages.Features.AddUser;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.AddUser;

public class AddUserCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly MessageQueueSender _queueSender;

    public AddUserCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        SetupUsersClient();
    }

    private AddUserCommandHandler CreateHandler(long userId)
    {
        return new AddUserCommandHandler(
            _h.ChatsStorage,
            _h.CreateUserContext(userId),
            _h.MessagesStorage,
            _usersClient.Object,
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<AddUserCommandHandler>());
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
    public async Task Handle_ValidAdd_AddsUser()
    {
        var adminId = 1L;
        var addedId = 99L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [adminId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        await handler.Handle(new AddUserCommand { ChatId = chat.Id, UserId = addedId }, CancellationToken.None);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.Should().Contain(m => m.UserId == addedId);
    }

    [Fact]
    public async Task Handle_NoAccessToChat_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1, 2]);
        await _h.SeedGroupChatInfo(chat.Id, 1, [1]);
        var handler = CreateHandler(99);

        var act = async () => await handler.Handle(new AddUserCommand { ChatId = chat.Id, UserId = 5 }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_NotGroupChat_ThrowsIsNotGroupChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: false, memberUserIds: [1, 2]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new AddUserCommand { ChatId = chat.Id, UserId = 99 }, CancellationToken.None);

        await act.Should().ThrowAsync<IsNotGroupChatException>();
    }

    [Fact]
    public async Task Handle_UserAlreadyMember_ThrowsUserAlreadyMemberChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1, 2]);
        await _h.SeedGroupChatInfo(chat.Id, 1, [1]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new AddUserCommand { ChatId = chat.Id, UserId = 2 }, CancellationToken.None);

        await act.Should().ThrowAsync<UserAlreadyMemberChatException>();
    }

    [Fact]
    public async Task Handle_NoPermission_ThrowsNoPermissionException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1, 2, 3]);
        await _h.SeedGroupChatInfo(chat.Id, 1, [1]);
        var handler = CreateHandler(3);

        var act = async () => await handler.Handle(new AddUserCommand { ChatId = chat.Id, UserId = 99 }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_ValidAdd_CreatesSystemMessage()
    {
        var adminId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [adminId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        await handler.Handle(new AddUserCommand { ChatId = chat.Id, UserId = 99 }, CancellationToken.None);

        var messages = _h.DbContext.Messages.ToList();
        messages.Should().ContainSingle(m => m.Type == Domain.MessageContentType.System);
    }

    [Fact]
    public async Task Handle_ValidAdd_PublishesMessageToQueue()
    {
        var adminId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [adminId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        await handler.Handle(new AddUserCommand { ChatId = chat.Id, UserId = 99 }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
