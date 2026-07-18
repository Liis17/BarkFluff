using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.KickUser;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.KickUser;

public class KickUserCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly MessageQueueSender _queueSender;

    public KickUserCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        SetupUsersClient();
    }

    private KickUserCommandHandler CreateHandler(long userId)
    {
        return new KickUserCommandHandler(
            _h.ChatsStorage,
            _h.CreateUserContext(userId),
            _h.MessagesStorage,
            _usersClient.Object,
            _queueSender,
            _h.PublishEndpointMock.Object,
            _h.Metrics,
            TestHelper.CreateLogger<KickUserCommandHandler>());
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
    public async Task Handle_ValidKick_RemovesUser()
    {
        var adminId = 1L;
        var kickedId = 2L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [adminId, kickedId, 3]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        await handler.Handle(new KickUserCommand { ChatId = chat.Id, UserId = kickedId }, CancellationToken.None);

        var members = await _h.ChatsStorage.GetChatMembers(chat.Id, 0, int.MaxValue);
        members.Should().NotContain(m => m.UserId == kickedId);
    }

    [Fact]
    public async Task Handle_NoAccessToChat_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1, 2]);
        await _h.SeedGroupChatInfo(chat.Id, 1, [1]);
        var handler = CreateHandler(99);

        var act = async () => await handler.Handle(new KickUserCommand { ChatId = chat.Id, UserId = 2 }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_NotGroupChat_ThrowsIsNotGroupChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: false, memberUserIds: [1, 2]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new KickUserCommand { ChatId = chat.Id, UserId = 2 }, CancellationToken.None);

        await act.Should().ThrowAsync<IsNotGroupChatException>();
    }

    [Fact]
    public async Task Handle_UserNotMember_ThrowsUserNotMemberChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1, 2]);
        await _h.SeedGroupChatInfo(chat.Id, 1, [1]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new KickUserCommand { ChatId = chat.Id, UserId = 99 }, CancellationToken.None);

        await act.Should().ThrowAsync<UserNotMemberChatException>();
    }

    [Fact]
    public async Task Handle_NoKickPermission_ThrowsNoPermissionException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [1, 2, 3]);
        await _h.SeedGroupChatInfo(chat.Id, 1, [1]);
        var handler = CreateHandler(3);

        var act = async () => await handler.Handle(new KickUserCommand { ChatId = chat.Id, UserId = 2 }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_ValidKick_CreatesSystemMessage()
    {
        var adminId = 1L;
        var kickedId = 2L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [adminId, kickedId]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        await handler.Handle(new KickUserCommand { ChatId = chat.Id, UserId = kickedId }, CancellationToken.None);

        var messages = _h.DbContext.Messages.ToList();
        messages.Should().ContainSingle(m => m.Type == Domain.MessageContentType.System);
    }

    [Fact]
    public async Task Handle_ValidKick_PublishesMessageToQueue()
    {
        var adminId = 1L;
        var kickedId = 2L;
        var chat = await _h.SeedChat(isGroupChat: true, memberUserIds: [adminId, kickedId]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        await handler.Handle(new KickUserCommand { ChatId = chat.Id, UserId = kickedId }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
