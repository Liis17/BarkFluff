using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.CheckChatMembership;

namespace BarkFluff.Messages.Tests.Features.CheckChatMembership;

public class CheckChatMembershipQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private CheckChatMembershipQueryHandler CreateHandler() => new(_h.ChatsStorage);

    [Fact]
    public async Task Handle_LocalChatByUserId_ReturnsMembershipWithoutFederatedContext()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery { UserId = 1, ChatIds = [chat.Id.ToString()] },
            CancellationToken.None);

        response.MemberChatIds.Should().ContainSingle().Which.Should().Be(chat.Id.ToString());
        response.FederatedChats.Should().BeEmpty();
        response.RequesterUuid.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForeignLocalChat_ReturnsEmpty()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);

        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery { UserId = 99, ChatIds = [chat.Id.ToString()] },
            CancellationToken.None);

        response.MemberChatIds.Should().BeEmpty();
        response.FederatedChats.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FederatedChatByUserId_ReturnsPeerAndRequesterUuid()
    {
        var localUuid = Guid.NewGuid();
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, localUuid, remoteUuid, "remote.test");

        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery { UserId = 1, ChatIds = [chat.Id.ToString()] },
            CancellationToken.None);

        response.MemberChatIds.Should().ContainSingle().Which.Should().Be(chat.Id.ToString());
        response.RequesterUuid.Should().Be(localUuid.ToString());

        var federated = response.FederatedChats.Should().ContainSingle().Subject;
        federated.ChatId.Should().Be(chat.Id.ToString());

        var peer = federated.Peers.Should().ContainSingle().Subject;
        peer.UserUuid.Should().Be(remoteUuid.ToString());
        peer.ServerName.Should().Be("remote.test");
    }

    [Fact]
    public async Task Handle_FederatedChatByUserUuid_ConfirmsRemoteMembership()
    {
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");

        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery { UserUuid = remoteUuid, ChatIds = [chat.Id.ToString()] },
            CancellationToken.None);

        response.MemberChatIds.Should().ContainSingle().Which.Should().Be(chat.Id.ToString());
        // Для uuid-ветки requester_uuid — эхо запрошенного.
        response.RequesterUuid.Should().Be(remoteUuid.ToString());
    }

    [Fact]
    public async Task Handle_ByUnknownUserUuid_ReturnsEmpty()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");

        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery { UserUuid = Guid.NewGuid(), ChatIds = [chat.Id.ToString()] },
            CancellationToken.None);

        response.MemberChatIds.Should().BeEmpty();
        response.FederatedChats.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FederatedStatus.Rejected)]
    [InlineData(FederatedStatus.Merged)]
    public async Task Handle_NonActiveFederatedChat_KeepsMembershipButDropsContext(FederatedStatus status)
    {
        // Чат существует — членство отдаём; маршрутизировать typing в него уже нельзя.
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test", status);

        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery { UserId = 1, ChatIds = [chat.Id.ToString()] },
            CancellationToken.None);

        response.MemberChatIds.Should().ContainSingle().Which.Should().Be(chat.Id.ToString());
        response.FederatedChats.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MixedChats_ReturnsContextOnlyForFederated()
    {
        var localChat = await _h.SeedChat(memberUserIds: [1]);
        var fedChat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");

        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery
            {
                UserId = 1,
                ChatIds = [localChat.Id.ToString(), fedChat.Id.ToString()],
            },
            CancellationToken.None);

        response.MemberChatIds.Should().BeEquivalentTo(
            [localChat.Id.ToString(), fedChat.Id.ToString()]);
        response.FederatedChats.Should().ContainSingle()
            .Which.ChatId.Should().Be(fedChat.Id.ToString());
    }

    [Fact]
    public async Task Handle_UnparsableChatIds_ReturnsEmptyResponse()
    {
        var response = await CreateHandler().Handle(
            new CheckChatMembershipQuery { UserId = 1, ChatIds = ["not-a-guid"] },
            CancellationToken.None);

        response.MemberChatIds.Should().BeEmpty();
        response.FederatedChats.Should().BeEmpty();
    }
}
