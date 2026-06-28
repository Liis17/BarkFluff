using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.AcceptPrivateChat;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.AcceptPrivateChat;

public class AcceptPrivateChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<PrivateChatInviteStore> _inviteStore;
    private readonly Mock<EncryptedMessageQueueSender> _queueSender;

    public AcceptPrivateChatCommandHandlerTests()
    {
        _inviteStore = new Mock<PrivateChatInviteStore>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>());
        _queueSender = new Mock<EncryptedMessageQueueSender>(_h.PublishEndpointMock.Object);
    }

    private AcceptPrivateChatCommandHandler CreateHandler(long userId)
    {
        return new AcceptPrivateChatCommandHandler(
            _h.ChatsStorage,
            _inviteStore.Object,
            _queueSender.Object,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<AcceptPrivateChatCommandHandler>());
    }

    [Fact]
    public async Task Handle_ValidAccept_AddsMemberAndRemovesInvite()
    {
        var inviterId = 1L;
        var inviteeId = 2L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [inviterId], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync(inviteeId);
        var handler = CreateHandler(inviteeId);

        var result = await handler.Handle(new AcceptPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        _inviteStore.Verify(s => s.RemoveAsync(chat.Id), Times.Once);
    }

    [Fact]
    public async Task Handle_ChatNotFound_ThrowsChatNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new AcceptPrivateChatCommand { ChatId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotFoundException>();
    }

    [Fact]
    public async Task Handle_NotPrivateChat_ThrowsChatNotPrivateException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new AcceptPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotPrivateException>();
    }

    [Fact]
    public async Task Handle_NoInvite_ThrowsPrivateChatInviteNotFoundException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync((long?)null);
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new AcceptPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<PrivateChatInviteNotFoundException>();
    }

    [Fact]
    public async Task Handle_WrongUser_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync(2L);
        var handler = CreateHandler(99);

        var act = async () => await handler.Handle(new AcceptPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_AlreadyAccepted_ThrowsPrivateChatAlreadyAcceptedException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync(2L);
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new AcceptPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<PrivateChatAlreadyAcceptedException>();
    }

    [Fact]
    public async Task Handle_PublishesInviteResolutionEvent()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        _inviteStore.Setup(s => s.GetInviteeAsync(chat.Id)).ReturnsAsync(2L);
        var handler = CreateHandler(2);

        await handler.Handle(new AcceptPrivateChatCommand { ChatId = chat.Id }, CancellationToken.None);

        _queueSender.Verify(q => q.SendInviteResolution(chat.Id, 1, 2, true), Times.Once);
    }
}
