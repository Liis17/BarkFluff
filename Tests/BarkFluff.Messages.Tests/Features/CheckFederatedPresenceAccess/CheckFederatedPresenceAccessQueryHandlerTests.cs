using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.CheckFederatedPresenceAccess;
using BarkFluff.Messages.Persistence.Services;

namespace BarkFluff.Messages.Tests.Features.CheckFederatedPresenceAccess;

public class CheckFederatedPresenceAccessQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private CheckFederatedPresenceAccessQueryHandler CreateHandler(ChatsStorage? storage = null)
        => new(storage ?? _h.ChatsStorage);

    private static CheckFederatedPresenceAccessQuery Query(string server, params Guid[] uuids)
        => new() { RequestingServer = server, UserUuids = uuids.Select(u => u.ToString()).ToList() };

    [Fact]
    public async Task Handle_ActiveChatWithRequestingServer_AllowsUuid()
    {
        var localUuid = Guid.NewGuid();
        await _h.SeedFederatedChat(1, localUuid, Guid.NewGuid(), "remote.test");

        var response = await CreateHandler().Handle(
            Query("remote.test", localUuid), CancellationToken.None);

        response.AllowedUserUuids.Should().ContainSingle().Which.Should().Be(localUuid.ToString());
    }

    [Fact]
    public async Task Handle_ChatWithDifferentServer_DeniesUuid()
    {
        var localUuid = Guid.NewGuid();
        await _h.SeedFederatedChat(1, localUuid, Guid.NewGuid(), "other.test");

        var response = await CreateHandler().Handle(
            Query("remote.test", localUuid), CancellationToken.None);

        response.AllowedUserUuids.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FederatedStatus.Rejected)]
    [InlineData(FederatedStatus.Merged)]
    public async Task Handle_NonActiveChat_DeniesUuid(FederatedStatus status)
    {
        var localUuid = Guid.NewGuid();
        await _h.SeedFederatedChat(1, localUuid, Guid.NewGuid(), "remote.test", status);

        var response = await CreateHandler().Handle(
            Query("remote.test", localUuid), CancellationToken.None);

        response.AllowedUserUuids.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownUuid_DeniesSilently()
    {
        await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");

        var response = await CreateHandler().Handle(
            Query("remote.test", Guid.NewGuid()), CancellationToken.None);

        // Не различаем «нет чата» и «нет пользователя» — существование аккаунтов не светим.
        response.AllowedUserUuids.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RemoteParticipantUuid_IsNotTreatedAsOurUser()
    {
        // Спрашивать можно только про НАШИХ пользователей: uuid remote-участника не разрешаем,
        // иначе нода получила бы presence чужих пользователей через наш чат.
        var remoteUuid = Guid.NewGuid();
        await _h.SeedFederatedChat(1, Guid.NewGuid(), remoteUuid, "remote.test");

        var response = await CreateHandler().Handle(
            Query("remote.test", remoteUuid), CancellationToken.None);

        response.AllowedUserUuids.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ServerNameCaseInsensitive_AllowsUuid()
    {
        var localUuid = Guid.NewGuid();
        await _h.SeedFederatedChat(1, localUuid, Guid.NewGuid(), "remote.test");

        var response = await CreateHandler().Handle(
            Query("  Remote.TEST  ", localUuid), CancellationToken.None);

        response.AllowedUserUuids.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_BatchOfUuids_HitsStorageOnce()
    {
        var uuids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        var storage = new Mock<ChatsStorage>(_h.DbContext) { CallBase = true };
        storage
            .Setup(s => s.GetUuidsSharingFederatedChatWithServer(
                It.IsAny<string>(), It.IsAny<List<Guid>>()))
            .ReturnsAsync([]);

        await CreateHandler(storage.Object).Handle(
            Query("remote.test", uuids), CancellationToken.None);

        storage.Verify(s => s.GetUuidsSharingFederatedChatWithServer(
            "remote.test", It.Is<List<Guid>>(l => l.Count == 50)), Times.Once);
    }

    [Fact]
    public async Task Handle_NoValidUuids_SkipsStorage()
    {
        var storage = new Mock<ChatsStorage>(_h.DbContext) { CallBase = true };

        var response = await CreateHandler(storage.Object).Handle(
            new CheckFederatedPresenceAccessQuery
            {
                RequestingServer = "remote.test",
                UserUuids = ["not-a-uuid"],
            },
            CancellationToken.None);

        response.AllowedUserUuids.Should().BeEmpty();
        storage.Verify(s => s.GetUuidsSharingFederatedChatWithServer(
            It.IsAny<string>(), It.IsAny<List<Guid>>()), Times.Never);
    }
}
