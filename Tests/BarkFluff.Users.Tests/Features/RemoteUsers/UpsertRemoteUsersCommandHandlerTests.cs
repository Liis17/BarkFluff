using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.UpsertRemoteUsers;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.RemoteUsers;

public class UpsertRemoteUsersCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_NewProfile_Ok()
    {
        var remoteUuid = Guid.NewGuid();
        var handler = new UpsertRemoteUsersCommandHandler(_h.RemoteUsersStorage, _h.Metrics);

        var response = await handler.Handle(new UpsertRemoteUsersCommand
        {
            Request = new UpsertRemoteUsersRequest
            {
                Records =
                {
                    new UpsertRemoteUserInfo
                    {
                        Uuid = remoteUuid.ToString(),
                        Username = "bob",
                        ServerName = "node2.test",
                        FirstName = "Bob",
                        LastName = "Smith",
                        Bio = "Hello",
                        AvatarFileId = "avatar-1",
                    }
                }
            }
        }, CancellationToken.None);

        response.Results.Should().HaveCount(1);
        response.Results[0].Ok.Should().BeTrue();
        response.Results[0].RejectReason.Should().BeEmpty();

        var stored = await _h.RemoteUsersStorage.GetAsync("bob", "node2.test");
        stored.Should().NotBeNull();
        stored!.Uuid.Should().Be(remoteUuid);
        stored.FirstName.Should().Be("Bob");
        stored.Bio.Should().Be("Hello");
    }

    [Fact]
    public async Task Handle_UuidMatchesLocalUser_Rejected()
    {
        // Вредоносная нода заявляет UUID нашего локального пользователя.
        var local = await _h.SeedUser(username: "localalice");
        var handler = new UpsertRemoteUsersCommandHandler(_h.RemoteUsersStorage, _h.Metrics);

        var response = await handler.Handle(new UpsertRemoteUsersCommand
        {
            Request = new UpsertRemoteUsersRequest
            {
                Records =
                {
                    new UpsertRemoteUserInfo
                    {
                        Uuid = local.Uuid.ToString(),
                        Username = "alice",
                        ServerName = "evil.test",
                    }
                }
            }
        }, CancellationToken.None);

        response.Results[0].Ok.Should().BeFalse();
        response.Results[0].RejectReason.Should().Be("LocalUuidCollision");
    }

    [Fact]
    public async Task Handle_UuidPinnedToOtherServer_Rejected()
    {
        var remoteUuid = Guid.NewGuid();
        await _h.SeedRemoteUser(uuid: remoteUuid, username: "bob", serverName: "node2.test");

        var handler = new UpsertRemoteUsersCommandHandler(_h.RemoteUsersStorage, _h.Metrics);

        // Тот же UUID, но другая нода заявляет его → пиннинг нарушен.
        var response = await handler.Handle(new UpsertRemoteUsersCommand
        {
            Request = new UpsertRemoteUsersRequest
            {
                Records =
                {
                    new UpsertRemoteUserInfo
                    {
                        Uuid = remoteUuid.ToString(),
                        Username = "bob",
                        ServerName = "evil.test",
                    }
                }
            }
        }, CancellationToken.None);

        response.Results[0].Ok.Should().BeFalse();
        response.Results[0].RejectReason.Should().Be("ServerNameMismatch");
    }

    [Fact]
    public async Task Handle_UsernameFreedOnOrigin_RenamedByFreshResolve()
    {
        // (Username, ServerName) уже занят другим UUID — username освободился/занялся на origin.
        var oldUuid = Guid.NewGuid();
        var newUuid = Guid.NewGuid();
        await _h.SeedRemoteUser(uuid: oldUuid, username: "carol", serverName: "node2.test");

        var handler = new UpsertRemoteUsersCommandHandler(_h.RemoteUsersStorage, _h.Metrics);

        var response = await handler.Handle(new UpsertRemoteUsersCommand
        {
            Request = new UpsertRemoteUsersRequest
            {
                Records =
                {
                    new UpsertRemoteUserInfo
                    {
                        Uuid = newUuid.ToString(),
                        Username = "carol",
                        ServerName = "node2.test",
                        FirstName = "Carol New",
                    }
                }
            }
        }, CancellationToken.None);

        response.Results[0].Ok.Should().BeTrue();

        var stored = await _h.RemoteUsersStorage.GetAsync("carol", "node2.test");
        stored.Should().NotBeNull();
        stored!.Uuid.Should().Be(newUuid);
        stored.FirstName.Should().Be("Carol New");
    }

    [Fact]
    public async Task Handle_IdempotentReUpsert_NoDuplicate()
    {
        var remoteUuid = Guid.NewGuid();
        var handler = new UpsertRemoteUsersCommandHandler(_h.RemoteUsersStorage, _h.Metrics);

        var request = new UpsertRemoteUsersCommand
        {
            Request = new UpsertRemoteUsersRequest
            {
                Records =
                {
                    new UpsertRemoteUserInfo
                    {
                        Uuid = remoteUuid.ToString(),
                        Username = "dave",
                        ServerName = "node2.test",
                        FirstName = "Dave",
                    }
                }
            }
        };

        await handler.Handle(request, CancellationToken.None);
        await handler.Handle(request, CancellationToken.None);

        var stored = await _h.RemoteUsersStorage.GetAsync("dave", "node2.test");
        stored.Should().NotBeNull();
        stored!.Uuid.Should().Be(remoteUuid);

        // Один ряд в кеше (PK по Uuid).
        var byUuid = await _h.RemoteUsersStorage.GetAsync(remoteUuid);
        byUuid.Should().NotBeNull();
        byUuid!.Username.Should().Be("dave");
    }

    [Fact]
    public async Task Handle_InvalidUuid_ReportedAsError()
    {
        var handler = new UpsertRemoteUsersCommandHandler(_h.RemoteUsersStorage, _h.Metrics);

        var response = await handler.Handle(new UpsertRemoteUsersCommand
        {
            Request = new UpsertRemoteUsersRequest
            {
                Records =
                {
                    new UpsertRemoteUserInfo
                    {
                        Uuid = "not-a-guid",
                        Username = "eve",
                        ServerName = "node2.test",
                    }
                }
            }
        }, CancellationToken.None);

        response.Results[0].Ok.Should().BeFalse();
        response.Results[0].RejectReason.Should().Be("InvalidUuid");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
