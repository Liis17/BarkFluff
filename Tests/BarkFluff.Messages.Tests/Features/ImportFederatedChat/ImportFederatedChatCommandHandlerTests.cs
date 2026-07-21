using BarkFluff.Messages.Features.ImportFederatedChat;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.ImportFederatedChat;

public class ImportFederatedChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;

    public ImportFederatedChatCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
    }

    private ImportFederatedChatCommandHandler CreateHandler(string ownServer = "home.test")
    {
        return new ImportFederatedChatCommandHandler(
            _h.ChatsStorage,
            _usersClient.Object,
            TestHelper.CreateConfiguration(ownServer),
            _h.Metrics,
            TestHelper.CreateLogger<ImportFederatedChatCommandHandler>());
    }

    private void SetupInvitee(bool found, bool isRemote, bool isDeactivated, long userId, bool denyFederatedDm = false)
    {
        var response = new GetUsersByUuidResponse
        {
            Users =
            {
                new UserProfileByUuid
                {
                    Found = found,
                    IsRemote = isRemote,
                    IsDeactivated = isDeactivated,
                    UserId = userId,
                    Username = "bob",
                    DenyFederatedDm = denyFederatedDm,
                },
            },
        };
        _usersClient
            .Setup(c => c.GetUsersByUuidAsync(It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetUsersByUuidResponse>(
                Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    private void SetupUpsertOk()
    {
        var response = new UpsertRemoteUsersResponse
        {
            Results = { new UpsertRemoteUserResult { Ok = true } },
        };
        _usersClient
            .Setup(c => c.UpsertRemoteUsersAsync(It.IsAny<UpsertRemoteUsersRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<UpsertRemoteUsersResponse>(
                Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    private static ImportFederatedChatRequest BuildRequest(Guid chatId, Guid initiatorUuid, Guid inviteeUuid)
        => new()
        {
            ChatId = chatId.ToString(),
            InitiatorUuid = initiatorUuid.ToString(),
            InitiatorUsername = "alice",
            InitiatorServerName = "remote.test",
            InviteeUuid = inviteeUuid.ToString(),
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

    [Fact]
    public async Task Handle_InviteeNotFound_ThrowsUnknownInviteeException()
    {
        SetupInvitee(found: false, isRemote: false, isDeactivated: false, userId: 0);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ImportFederatedChatCommand(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())), CancellationToken.None);

        await act.Should().ThrowAsync<UnknownInviteeException>();
    }

    [Fact]
    public async Task Handle_InviteeDenyFederatedDm_ThrowsFederatedDmRejectedException()
    {
        // Этап 2.5: invitee запретил входящие fed-DM — новый чат отклоняется.
        SetupInvitee(found: true, isRemote: false, isDeactivated: false, userId: 1, denyFederatedDm: true);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ImportFederatedChatCommand(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())), CancellationToken.None);

        await act.Should().ThrowAsync<FederatedDmRejectedException>();
    }

    [Fact]
    public async Task Handle_ValidInvitee_CreatesChatAndReturnsImportedTrue()
    {
        SetupInvitee(found: true, isRemote: false, isDeactivated: false, userId: 1);
        SetupUpsertOk();
        var handler = CreateHandler();
        var chatId = Guid.NewGuid();

        var response = await handler.Handle(new ImportFederatedChatCommand(
            BuildRequest(chatId, Guid.NewGuid(), Guid.NewGuid())), CancellationToken.None);

        response.Imported.Should().BeTrue();
        var chat = await _h.ChatsStorage.GetFederatedChatAsync(chatId);
        chat.Should().NotBeNull();
        chat!.FederatedStatus.Should().Be(Domain.FederatedStatus.Active);
    }

    [Fact]
    public async Task Handle_ExistingChat_IdempotentEvenWithDenyFederatedDmNowTrue()
    {
        // "Только новые чаты": повторная доставка ChatCreated для уже импортированного чата
        // не должна упасть, даже если invitee включил запрет ПОСЛЕ создания чата.
        var initiatorUuid = Guid.NewGuid();
        var inviteeUuid = Guid.NewGuid();
        var existing = await _h.SeedFederatedChat(1, inviteeUuid, initiatorUuid, "remote.test");

        SetupInvitee(found: true, isRemote: false, isDeactivated: false, userId: 1, denyFederatedDm: true);
        var handler = CreateHandler();

        var response = await handler.Handle(new ImportFederatedChatCommand(
            BuildRequest(existing.Id, initiatorUuid, inviteeUuid)), CancellationToken.None);

        response.Imported.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ConcurrentImport_ThrowsDuplicateFederatedDmException()
    {
        // Баг #9b: два ChatCreated для одной UUID-пары с разными ChatId, обработанные почти
        // одновременно — CreateFederatedChatAsync под конфликтом уникального индекса пары
        // внутренне возвращает чат ПОБЕДИТЕЛЯ (другой Id). Молчаливый Imported=true был бы неверен:
        // конкретный запрошенный chatId так и не сохранился — переиспользуем уже существующее
        // DuplicateFederatedDmException вместо тихого успеха.
        SetupInvitee(found: true, isRemote: false, isDeactivated: false, userId: 1);
        SetupUpsertOk();

        var winnerChat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");

        var chatsStorageMock = new Mock<ChatsStorage>(_h.DbContext) { CallBase = true };
        chatsStorageMock
            .Setup(s => s.CreateFederatedChatAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(winnerChat);

        var handler = new ImportFederatedChatCommandHandler(
            chatsStorageMock.Object,
            _usersClient.Object,
            TestHelper.CreateConfiguration("home.test"),
            _h.Metrics,
            TestHelper.CreateLogger<ImportFederatedChatCommandHandler>());

        var act = async () => await handler.Handle(new ImportFederatedChatCommand(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateFederatedDmException>();
    }
}
