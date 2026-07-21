using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.GetUsersByUuid;

namespace BarkFluff.Users.Tests.Features.GetUsersByUuid;

public class GetUsersByUuidQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private GetUsersByUuidQueryHandler CreateHandler()
    {
        return new GetUsersByUuidQueryHandler(
            _h.UsersStorage,
            _h.RemoteUsersStorage,
            _h.PrivacyStorage,
            _h.Metrics);
    }

    private async Task<BarkFluff.Users.Domain.User> SeedLocalUserWithUuid(Guid uuid, string username = "alice")
    {
        var user = await _h.SeedUser(username: username);
        user.Uuid = uuid;
        await _h.DbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_LocalUserWithoutPrivacyRow_DenyFederatedDmDefaultsFalse()
    {
        // Privacy ещё не создана (GetOrCreate ленивый) — отсутствие строки не должно ломать
        // резолв invitee в ImportFederatedChat: по умолчанию федеративные DM разрешены.
        var uuid = Guid.NewGuid();
        await SeedLocalUserWithUuid(uuid);
        var handler = CreateHandler();

        var response = await handler.Handle(new GetUsersByUuidQuery
        {
            Request = new GetUsersByUuidRequest { Uuids = { uuid.ToString() } },
        }, CancellationToken.None);

        var user = response.Users.Should().ContainSingle().Subject;
        user.Found.Should().BeTrue();
        user.DenyFederatedDm.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LocalUserWithDenyFederatedDmTrue_ReturnsTrue()
    {
        var uuid = Guid.NewGuid();
        var user = await SeedLocalUserWithUuid(uuid);
        await _h.SeedPrivacy(user.Id);
        var privacy = await _h.PrivacyStorage.Get(user.Id);
        privacy!.DenyFederatedDm = true;
        await _h.DbContext.SaveChangesAsync();
        var handler = CreateHandler();

        var response = await handler.Handle(new GetUsersByUuidQuery
        {
            Request = new GetUsersByUuidRequest { Uuids = { uuid.ToString() } },
        }, CancellationToken.None);

        response.Users.Should().ContainSingle().Which.DenyFederatedDm.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RemoteUser_DenyFederatedDmAlwaysFalse()
    {
        var remote = await _h.SeedRemoteUser();
        var handler = CreateHandler();

        var response = await handler.Handle(new GetUsersByUuidQuery
        {
            Request = new GetUsersByUuidRequest { Uuids = { remote.Uuid.ToString() } },
        }, CancellationToken.None);

        var user = response.Users.Should().ContainSingle().Subject;
        user.IsRemote.Should().BeTrue();
        user.DenyFederatedDm.Should().BeFalse();
    }
}
