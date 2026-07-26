using BarkFluff.Onliner.Features.SetTypingStatus;
using BarkFluff.Onliner.Messages;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Onliner;

using Grpc.Core;

using MassTransit;

using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.Onliner.Tests.Features.SetTypingStatus;

/// <summary>
/// Исходящая ветка typing в федерацию (этап 4.4). Ключевое: локальный путь не меняется
/// и от федерации не зависит.
/// </summary>
public class FederatedTypingTests
{
    private const long UserId = 1;

    private readonly TestHelper _h = new();
    private readonly Mock<MessagesServerApi.MessagesServerApiClient> _messages = new();
    private readonly Mock<FederationInternalApi.FederationInternalApiClient> _federation = new();
    private readonly List<DeliverTypingOutboundRequest> _sent = [];
    private readonly string _chatId = Guid.NewGuid().ToString();

    private void SetupMembership(CheckChatMembershipResponse response)
    {
        _messages
            .Setup(c => c.CheckChatMembershipAsync(
                It.IsAny<CheckChatMembershipRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<CheckChatMembershipResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private CheckChatMembershipResponse LocalChat()
    {
        var response = new CheckChatMembershipResponse();
        response.MemberChatIds.Add(_chatId);
        return response;
    }

    private CheckChatMembershipResponse FederatedChat(Guid requesterUuid, params (Guid Uuid, string Server)[] peers)
    {
        var response = new CheckChatMembershipResponse { RequesterUuid = requesterUuid.ToString() };
        response.MemberChatIds.Add(_chatId);

        var federated = new FederatedChatContext { ChatId = _chatId };
        foreach (var (uuid, server) in peers)
        {
            federated.Peers.Add(new FederatedChatPeer { UserUuid = uuid.ToString(), ServerName = server });
        }
        response.FederatedChats.Add(federated);

        return response;
    }

    private void SetupFederationOk()
    {
        _federation
            .Setup(c => c.DeliverTypingOutboundAsync(
                It.IsAny<DeliverTypingOutboundRequest>(), null, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<DeliverTypingOutboundRequest, Metadata, DateTime?, CancellationToken>(
                (r, _, _, _) => _sent.Add(r))
            .Returns(new AsyncUnaryCall<DeliverTypingOutboundResponse>(
                Task.FromResult(new DeliverTypingOutboundResponse()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private SetTypingStatusCommandHandler CreateHandler(bool federationConfigured = true)
    {
        var services = new ServiceCollection();
        if (federationConfigured)
        {
            services.AddSingleton(_federation.Object);
        }

        var sender = new FederatedTypingSender(
            services.BuildServiceProvider(),
            _h.Metrics,
            TestHelper.CreateLogger<FederatedTypingSender>());

        var filter = new ChatMembershipFilter(
            _messages.Object, _h.Metrics, TestHelper.CreateLogger<ChatMembershipFilter>());

        return new SetTypingStatusCommandHandler(
            _h.CreateUserContext(UserId),
            _h.PublishEndpointMock.Object,
            filter,
            sender,
            _h.Metrics);
    }

    private Task<SetTypingStatusResponse> HandleAsync(SetTypingStatusCommandHandler handler)
        => handler.Handle(
            new SetTypingStatusCommand { ChatId = _chatId, Action = TypingAction.Typing },
            CancellationToken.None);

    [Fact]
    public async Task LocalChat_DoesNotCallFederation()
    {
        // Подавляющее большинство вызовов — локальные чаты; лишней работы быть не должно.
        SetupMembership(LocalChat());
        SetupFederationOk();

        await HandleAsync(CreateHandler());

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task FederatedChat_SendsSenderUuidAndDestinations()
    {
        var requesterUuid = Guid.NewGuid();
        SetupMembership(FederatedChat(requesterUuid,
            (Guid.NewGuid(), "node-b.test"),
            (Guid.NewGuid(), "node-c.test")));
        SetupFederationOk();

        await HandleAsync(CreateHandler());

        var request = _sent.Should().ContainSingle().Subject;
        request.ChatId.Should().Be(_chatId);
        request.SenderUuid.Should().Be(requesterUuid.ToString());
        request.Action.Should().Be((int)TypingAction.Typing);
        request.DestinationServers.Should().BeEquivalentTo(["node-b.test", "node-c.test"]);
    }

    [Fact]
    public async Task FederatedChat_DuplicateServers_AreCollapsed()
    {
        SetupMembership(FederatedChat(Guid.NewGuid(),
            (Guid.NewGuid(), "node-b.test"),
            (Guid.NewGuid(), "node-b.test")));
        SetupFederationOk();

        await HandleAsync(CreateHandler());

        _sent.Should().ContainSingle().Which.DestinationServers.Should().BeEquivalentTo(["node-b.test"]);
    }

    [Fact]
    public async Task FederatedChat_WithoutRequesterUuid_SkipsFederation()
    {
        // У отправителя нет uuid — федерировать нечего.
        var response = new CheckChatMembershipResponse();
        response.MemberChatIds.Add(_chatId);
        var federated = new FederatedChatContext { ChatId = _chatId };
        federated.Peers.Add(new FederatedChatPeer { UserUuid = Guid.NewGuid().ToString(), ServerName = "node-b.test" });
        response.FederatedChats.Add(federated);

        SetupMembership(response);
        SetupFederationOk();

        await HandleAsync(CreateHandler());

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task FederationUnavailable_DoesNotBreakLocalTyping()
    {
        // Недоступность федерации не должна ломать локальный typing и не должна пробрасываться клиенту.
        SetupMembership(FederatedChat(Guid.NewGuid(), (Guid.NewGuid(), "node-b.test")));
        _federation
            .Setup(c => c.DeliverTypingOutboundAsync(
                It.IsAny<DeliverTypingOutboundRequest>(), null, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "boom")));

        var act = async () => await HandleAsync(CreateHandler());

        await act.Should().NotThrowAsync();

        // Локальный fan-out при этом отработал как обычно.
        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<TypingChangedEvent>(e => e.ChatId == _chatId && e.UserId == UserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FederationNotConfigured_BranchIsInactive()
    {
        // Нода без федерации не поднимает клиента вовсе — ветка не активируется.
        SetupMembership(FederatedChat(Guid.NewGuid(), (Guid.NewGuid(), "node-b.test")));
        SetupFederationOk();

        await HandleAsync(CreateHandler(federationConfigured: false));

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task NonMember_IsRejectedBeforeFederation()
    {
        // Локальная проверка членства остаётся первой линией.
        SetupMembership(new CheckChatMembershipResponse());
        SetupFederationOk();

        await HandleAsync(CreateHandler());

        _sent.Should().BeEmpty();
        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.IsAny<TypingChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
