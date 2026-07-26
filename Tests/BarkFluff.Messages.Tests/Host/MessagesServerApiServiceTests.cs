using BarkFluff.Messages.Features.CheckChatMembership;
using BarkFluff.Messages.Features.CheckFederatedPresenceAccess;
using BarkFluff.Messages.Host;
using BarkFluff.Proto.Messages;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Messages.Tests.Host;

public class MessagesServerApiServiceTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly MessagesServerApiService _service;

    public MessagesServerApiServiceTests()
    {
        _service = new MessagesServerApiService(_mediator.Object);
        _mediator
            .Setup(m => m.Send(It.IsAny<CheckChatMembershipQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckChatMembershipResponse());
        _mediator
            .Setup(m => m.Send(It.IsAny<CheckFederatedPresenceAccessQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckFederatedPresenceAccessResponse());
    }

    [Fact]
    public async Task CheckChatMembership_OnlyUserId_MapsAsBefore()
    {
        // Регрессия контракта: старый вызов (user_id + chat_ids) обязан работать без изменений.
        var chatId = Guid.NewGuid().ToString();
        var request = new CheckChatMembershipRequest { UserId = 42 };
        request.ChatIds.Add(chatId);

        await _service.CheckChatMembership(request, new TestServerCallContext());

        _mediator.Verify(m => m.Send(
            It.Is<CheckChatMembershipQuery>(q =>
                q.UserId == 42 && q.UserUuid == null && q.ChatIds.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckChatMembership_UserUuidGiven_MapsToUuidBranch()
    {
        var uuid = Guid.NewGuid();
        var request = new CheckChatMembershipRequest { UserId = 0, UserUuid = uuid.ToString() };
        request.ChatIds.Add(Guid.NewGuid().ToString());

        await _service.CheckChatMembership(request, new TestServerCallContext());

        _mediator.Verify(m => m.Send(
            It.Is<CheckChatMembershipQuery>(q => q.UserId == null && q.UserUuid == uuid),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckChatMembership_NoIdentifier_ThrowsInvalidArgument()
    {
        var request = new CheckChatMembershipRequest { UserId = 0 };
        request.ChatIds.Add(Guid.NewGuid().ToString());

        var act = async () => await _service.CheckChatMembership(request, new TestServerCallContext());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task CheckChatMembership_MalformedUuid_ThrowsInvalidArgument()
    {
        var request = new CheckChatMembershipRequest { UserId = 0, UserUuid = "not-a-uuid" };

        var act = async () => await _service.CheckChatMembership(request, new TestServerCallContext());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task CheckFederatedPresenceAccess_EmptyServer_ThrowsInvalidArgument()
    {
        var request = new CheckFederatedPresenceAccessRequest { RequestingServer = "  " };

        var act = async () => await _service.CheckFederatedPresenceAccess(
            request, new TestServerCallContext());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task CheckFederatedPresenceAccess_OverLimit_ThrowsInvalidArgument()
    {
        var request = new CheckFederatedPresenceAccessRequest { RequestingServer = "remote.test" };
        request.UserUuids.AddRange(Enumerable
            .Range(0, CheckFederatedPresenceAccessQuery.MaxUserUuids + 1)
            .Select(_ => Guid.NewGuid().ToString()));

        var act = async () => await _service.CheckFederatedPresenceAccess(
            request, new TestServerCallContext());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task CheckFederatedPresenceAccess_AtLimit_IsAccepted()
    {
        var request = new CheckFederatedPresenceAccessRequest { RequestingServer = "remote.test" };
        request.UserUuids.AddRange(Enumerable
            .Range(0, CheckFederatedPresenceAccessQuery.MaxUserUuids)
            .Select(_ => Guid.NewGuid().ToString()));

        await _service.CheckFederatedPresenceAccess(request, new TestServerCallContext());

        _mediator.Verify(m => m.Send(
            It.IsAny<CheckFederatedPresenceAccessQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
