using BarkFluff.Messages.Features.CreatePrivateChat;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.CreatePrivateChat;

public class CreatePrivateChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<PrivateChatInviteStore> _inviteStore;
    private readonly Mock<EncryptedMessageQueueSender> _queueSender;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;

    public CreatePrivateChatCommandHandlerTests()
    {
        _inviteStore = new Mock<PrivateChatInviteStore>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>());
        _queueSender = new Mock<EncryptedMessageQueueSender>(_h.PublishEndpointMock.Object);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        SetupUsersClient();
    }

    private CreatePrivateChatCommandHandler CreateHandler(long userId)
    {
        return new CreatePrivateChatCommandHandler(
            _h.ChatsStorage,
            _inviteStore.Object,
            _queueSender.Object,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<CreatePrivateChatCommandHandler>());
    }

    private void SetupUsersClient()
    {
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(new GetByIdResponse {                 User = new User { Id = 2, FirstName = "Peer", LastName = "User" } }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesPrivateChat()
    {
        var handler = CreateHandler(1);
        var salt = new byte[32];
        var verifier = new byte[32];

        var result = await handler.Handle(new CreatePrivateChatCommand
        {
            PeerUserId = 2,
            KdfSalt = salt,
            PassphraseVerifier = verifier
        }, CancellationToken.None);

        result.Should().NotBeNull();
        Guid.TryParse(result.Chat.Id, out var chatGuid).Should().BeTrue();
        var dbChat = await _h.ChatsStorage.GetChat(chatGuid);
        dbChat!.Type.Should().Be(Domain.ChatType.Private);
    }

    [Fact]
    public async Task Handle_SelfChat_ThrowsSourceForSendMessageNotSetException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreatePrivateChatCommand
        {
            PeerUserId = 1,
            KdfSalt = new byte[32],
            PassphraseVerifier = new byte[32]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<SourceForSendMessageNotSetException>();
    }

    [Fact]
    public async Task Handle_SaltTooShort_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreatePrivateChatCommand
        {
            PeerUserId = 2,
            KdfSalt = new byte[10],
            PassphraseVerifier = new byte[32]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_SaltTooLong_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreatePrivateChatCommand
        {
            PeerUserId = 2,
            KdfSalt = new byte[65],
            PassphraseVerifier = new byte[32]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_VerifierTooShort_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreatePrivateChatCommand
        {
            PeerUserId = 2,
            KdfSalt = new byte[32],
            PassphraseVerifier = new byte[10]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_SetsInviteInStore()
    {
        var handler = CreateHandler(1);

        await handler.Handle(new CreatePrivateChatCommand
        {
            PeerUserId = 2,
            KdfSalt = new byte[32],
            PassphraseVerifier = new byte[32]
        }, CancellationToken.None);

        _inviteStore.Verify(s => s.SetAsync(It.IsAny<Guid>(), 2), Times.Once);
    }

    [Fact]
    public async Task Handle_PublishesInviteEvent()
    {
        var handler = CreateHandler(1);

        await handler.Handle(new CreatePrivateChatCommand
        {
            PeerUserId = 2,
            KdfSalt = new byte[32],
            PassphraseVerifier = new byte[32]
        }, CancellationToken.None);

        _queueSender.Verify(q => q.SendInvite(It.IsAny<Guid>(), 1, 2, It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<DateTime>()), Times.Once);
    }
}
