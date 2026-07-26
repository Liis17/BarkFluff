using BarkFluff.Users.Domain;
using BarkFluff.Users.Features.CheckRemoteAvatarRef;
using BarkFluff.Users.Features.IsAvatarVisibleToFederation;

namespace BarkFluff.Users.Tests.Features.RemoteAvatars;

/// <summary>
/// Аватары в федерации (этап 3.4): privacy на origin и anti-open-proxy на приёме.
/// </summary>
public class RemoteAvatarAccessTests
{
    private readonly TestHelper _h = new();

    private Task<bool> IsVisibleAsync(long userId)
        => new IsAvatarVisibleToFederationQueryHandler(_h.UsersStorage, _h.PrivacyStorage)
            .Handle(new IsAvatarVisibleToFederationQuery { UserId = userId }, CancellationToken.None)
            .ContinueWith(t => t.Result.Visible);

    private Task<bool> RefExistsAsync(string serverName, string fileId)
        => new CheckRemoteAvatarRefQueryHandler(_h.RemoteUsersStorage)
            .Handle(
                new CheckRemoteAvatarRefQuery { ServerName = serverName, FileId = fileId },
                CancellationToken.None)
            .ContinueWith(t => t.Result.Exists);

    // ---- IsAvatarVisibleToFederation ----

    [Fact]
    public async Task AvatarVisibilityAll_IsVisible()
    {
        var user = await _h.SeedUser(profilePicture: Guid.NewGuid().ToString());
        await _h.SeedPrivacy(user.Id, avatarVisibility: ProfileFieldVisibility.All);

        (await IsVisibleAsync(user.Id)).Should().BeTrue();
    }

    [Theory]
    [InlineData(ProfileFieldVisibility.None)]
    [InlineData(ProfileFieldVisibility.Friends)]
    public async Task AvatarHidden_IsNotVisible(ProfileFieldVisibility visibility)
    {
        // Friends трактуется как None, пока нет сервиса отношений.
        var user = await _h.SeedUser(profilePicture: Guid.NewGuid().ToString());
        await _h.SeedPrivacy(user.Id, avatarVisibility: visibility);

        (await IsVisibleAsync(user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task ProfileHiddenOnSite_HidesAvatarToo()
    {
        // Инвариант: GetFederatedProfile при скрытом профиле возвращает found=false и аватар
        // не отдаёт — файл обязан вести себя так же.
        var user = await _h.SeedUser(profilePicture: Guid.NewGuid().ToString());
        await _h.SeedPrivacy(user.Id, profileVisibleOnSite: false, avatarVisibility: ProfileFieldVisibility.All);

        (await IsVisibleAsync(user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownUser_IsNotVisible()
    {
        (await IsVisibleAsync(999999)).Should().BeFalse();
    }

    [Fact]
    public async Task DraftUser_IsNotVisible()
    {
        var user = await _h.SeedUser(isDraft: true, profilePicture: Guid.NewGuid().ToString());
        await _h.SeedPrivacy(user.Id, avatarVisibility: ProfileFieldVisibility.All);

        (await IsVisibleAsync(user.Id)).Should().BeFalse();
    }

    // ---- CheckRemoteAvatarRef (anti-open-proxy) ----

    [Fact]
    public async Task KnownAvatarRef_Exists()
    {
        var fileId = Guid.NewGuid().ToString();
        await _h.SeedRemoteUser(serverName: "node2.test", avatarFileId: fileId);

        (await RefExistsAsync("node2.test", fileId)).Should().BeTrue();
    }

    [Fact]
    public async Task RandomFileId_DoesNotExist()
    {
        // Без этой проверки маршрут проксировал бы произвольный файл с известной ноды.
        await _h.SeedRemoteUser(serverName: "node2.test", avatarFileId: Guid.NewGuid().ToString());

        (await RefExistsAsync("node2.test", Guid.NewGuid().ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task SameFileIdOnAnotherServer_DoesNotExist()
    {
        var fileId = Guid.NewGuid().ToString();
        await _h.SeedRemoteUser(serverName: "node2.test", avatarFileId: fileId);

        (await RefExistsAsync("evil.test", fileId)).Should().BeFalse();
    }

    [Fact]
    public async Task ServerNameCaseInsensitive_Exists()
    {
        var fileId = Guid.NewGuid().ToString();
        await _h.SeedRemoteUser(serverName: "node2.test", avatarFileId: fileId);

        (await RefExistsAsync("  Node2.TEST ", fileId)).Should().BeTrue();
    }

    [Fact]
    public async Task EmptyInput_DoesNotExist()
    {
        (await RefExistsAsync("  ", Guid.NewGuid().ToString())).Should().BeFalse();
        (await RefExistsAsync("node2.test", "  ")).Should().BeFalse();
    }

    [Fact]
    public async Task ChangedAvatar_OldFileIdStopsWorking()
    {
        // Смена аватара на origin обновляет RemoteUsers.AvatarFileId — старая ссылка
        // перестаёт проходить проверку. Кешей нет, поведение согласованное.
        var oldFileId = Guid.NewGuid().ToString();
        var remote = await _h.SeedRemoteUser(serverName: "node2.test", avatarFileId: oldFileId);

        var newFileId = Guid.NewGuid().ToString();
        remote.AvatarFileId = newFileId;
        await _h.DbContext.SaveChangesAsync();

        (await RefExistsAsync("node2.test", newFileId)).Should().BeTrue();
        (await RefExistsAsync("node2.test", oldFileId)).Should().BeFalse();
    }
}
