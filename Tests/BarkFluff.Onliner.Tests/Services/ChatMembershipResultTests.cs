using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Messages;

namespace BarkFluff.Onliner.Tests.Services;

public class ChatMembershipResultTests
{
    [Fact]
    public void FromResponse_LocalChatsOnly_HasNoFederatedContext()
    {
        var chatId = Guid.NewGuid().ToString();
        var response = new CheckChatMembershipResponse();
        response.MemberChatIds.Add(chatId);

        var result = ChatMembershipResult.FromResponse(response);

        result.MemberChatIds.Should().BeEquivalentTo([chatId]);
        result.FederatedChats.Should().BeEmpty();
        result.RequesterUuid.Should().BeNull();
    }

    [Fact]
    public void FromResponse_FederatedChat_MapsPeersAndRequesterUuid()
    {
        var chatId = Guid.NewGuid().ToString();
        var requesterUuid = Guid.NewGuid();
        var peerUuid = Guid.NewGuid();

        var response = new CheckChatMembershipResponse { RequesterUuid = requesterUuid.ToString() };
        response.MemberChatIds.Add(chatId);

        var federated = new FederatedChatContext { ChatId = chatId };
        federated.Peers.Add(new FederatedChatPeer
        {
            UserUuid = peerUuid.ToString(),
            ServerName = "remote.test",
        });
        response.FederatedChats.Add(federated);

        var result = ChatMembershipResult.FromResponse(response);

        result.RequesterUuid.Should().Be(requesterUuid);
        result.FederatedChats.Should().ContainKey(chatId);
        result.FederatedChats[chatId].Should().ContainSingle()
            .Which.Should().Be(new FederatedChatPeerInfo(peerUuid, "remote.test"));
    }

    [Fact]
    public void FromResponse_MalformedPeer_IsDropped()
    {
        var chatId = Guid.NewGuid().ToString();
        var response = new CheckChatMembershipResponse();
        response.MemberChatIds.Add(chatId);

        var federated = new FederatedChatContext { ChatId = chatId };
        federated.Peers.Add(new FederatedChatPeer { UserUuid = "not-a-uuid", ServerName = "remote.test" });
        federated.Peers.Add(new FederatedChatPeer { UserUuid = Guid.NewGuid().ToString(), ServerName = "" });
        response.FederatedChats.Add(federated);

        var result = ChatMembershipResult.FromResponse(response);

        result.FederatedChats[chatId].Should().BeEmpty();
    }

    [Fact]
    public void Empty_IsFailClosed()
    {
        ChatMembershipResult.Empty.MemberChatIds.Should().BeEmpty();
        ChatMembershipResult.Empty.FederatedChats.Should().BeEmpty();
        ChatMembershipResult.Empty.RequesterUuid.Should().BeNull();
    }
}
