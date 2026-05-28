using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.RejectPrivateChat;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.RejectPrivateChat;

public class RejectPrivateChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<PrivateChatInviteStore> _inviteStore;
    private readonly Mock<EncryptedMessageQueueSender> _queueSender;

    public RejectPrivateChatCommandHandlerTests()
    {
        _inviteStore = new Mock<PrivateChatInviteStore>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>());
        _queueSender = new Mock<EncryptedMessageQueueSender>(_h.PublishEndpointMock.Object);
    }

    private RejectPrivateChatCommandHandler CreateHandler(long userId)
    {
        return new RejectPrivateChatCommandHandler(
            _h.ChatsStorage,
            _inviteStore.Object,
            _queueSender.Object,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<RejectPrivateChatCommandHandler>());
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_ValidReject_DeletesChatAndInvite()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync(2L);
        _inviteStore.Setup(s => s.RemoveAsync(chat.Id)).ReturnsAsync(true);
        var handler = CreateHandler(2);

        var result = await handler.Handle(new RejectPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        _inviteStore.Verify(s => s.RemoveAsync(chat.Id), Times.Once);
        _queueSender.Verify(q => q.SendInviteResolution(chat.Id, 1, 2, false), Times.Once);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_ChatNotFound_ThrowsChatNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new RejectPrivateChatCommand { ChatId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotFoundException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_NotPrivateChat_ThrowsChatNotPrivateException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new RejectPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotPrivateException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_NoInvite_ThrowsPrivateChatInviteNotFoundException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync((long?)null);
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new RejectPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<PrivateChatInviteNotFoundException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_WrongUser_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync(2L);
        var handler = CreateHandler(99);

        var act = async () => await handler.Handle(new RejectPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }
}
