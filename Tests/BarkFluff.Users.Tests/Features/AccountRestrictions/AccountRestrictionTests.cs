using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Domain;
using BarkFluff.Users.Features.AccountRestrictions;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;

using FluentAssertions;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

using UserAccountStatus = BarkFluff.Users.Domain.AccountStatus;
using AccountRestrictionAction = BarkFluff.Proto.Users.AccountRestrictionAction;

namespace BarkFluff.Users.Tests.Features.AccountRestrictions;

public class AccountRestrictionTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task SetRestriction_AppliesAllowedTransitionsAndPreservesDatesOnRetry()
    {
        var setup = await CreateSqliteUser();
        await using var connection = setup.Connection;
        await using var db = setup.Context;
        var user = setup.User;
        var handler = new SetAccountRestrictionCommandHandler(new UsersStorage(db), _h.QueueSender);

        var blocked = await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Block, "abuse"), CancellationToken.None);
        blocked.Restriction.Status.Should().Be(BarkFluff.Proto.Users.AccountStatus.Blocked);
        blocked.Restriction.Reason.Should().Be("abuse");
        var restrictedAt = blocked.Restriction.RestrictedAt;
        var repeated = await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Block, "new reason"), CancellationToken.None);
        repeated.Restriction.RestrictedAt.Should().Be(restrictedAt);
        repeated.Restriction.Reason.Should().Be("abuse");

        var pending = await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.ScheduleDeletion, "requested"), CancellationToken.None);
        pending.Restriction.Status.Should().Be(BarkFluff.Proto.Users.AccountStatus.PendingDeletion);
        pending.Restriction.DeletionScheduledAt!.ToDateTime().Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(2));

        var cancelled = await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.CancelDeletion, "changed mind"), CancellationToken.None);
        cancelled.Restriction.Status.Should().Be(BarkFluff.Proto.Users.AccountStatus.Active);
        cancelled.Restriction.Reason.Should().BeEmpty();
        cancelled.Restriction.DeletionScheduledAt.Should().BeNull();
    }

    [Fact]
    public async Task SetRestriction_AllowsBlockedAccountToScheduleDeletionAndUnblockIsIdempotent()
    {
        var setup = await CreateSqliteUser();
        await using var connection = setup.Connection;
        await using var db = setup.Context;
        var user = setup.User;
        var handler = new SetAccountRestrictionCommandHandler(new UsersStorage(db), _h.QueueSender);

        await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Block, "reason"), CancellationToken.None);
        var scheduled = await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.ScheduleDeletion, "reason"), CancellationToken.None);

        scheduled.Restriction.Status.Should().Be(BarkFluff.Proto.Users.AccountStatus.PendingDeletion);
        await FluentActions.Invoking(() => handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Unblock, "reason"), CancellationToken.None))
            .Should().ThrowAsync<InvalidAccountRestrictionTransitionException>();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SetRestriction_RejectsDraftsAndBots(bool draft, bool bot)
    {
        var user = await _h.SeedUser(isDraft: draft, isBot: bot);
        var handler = CreateSetHandler();

        await FluentActions.Invoking(() => handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Block, "reason"), CancellationToken.None))
            .Should().ThrowAsync<InvalidAccountRestrictionTransitionException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task SetRestriction_RequiresBoundedReasonForBlock(int reasonLength)
    {
        var user = await _h.SeedUser();
        var handler = CreateSetHandler();
        var reason = new string('x', reasonLength);

        await FluentActions.Invoking(() => handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Block, reason), CancellationToken.None))
            .Should().ThrowAsync<InvalidAccountRestrictionTransitionException>();
    }

    [Fact]
    public async Task SetRestriction_RepeatsNameAndAvatarEventsForIdempotentRetry()
    {
        var setup = await CreateSqliteUser();
        await using var connection = setup.Connection;
        await using var db = setup.Context;
        var user = setup.User;
        var handler = new SetAccountRestrictionCommandHandler(new UsersStorage(db), _h.QueueSender);

        await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Block, "reason"), CancellationToken.None);
        await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.Block, "reason"), CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(
                It.Is<BarkFluff.Shared.Queue.Users.UserChangedName>(e => e.UserId == user.Id && e.NewFirstName == "Заблокированный пользователь"),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        _h.PublishEndpointMock.Verify(p => p.Publish(
                It.Is<BarkFluff.Shared.Queue.Users.UserChangedAvatar>(e => e.UserId == user.Id && e.ProfilePictureUrl == string.Empty),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ClaimAccountsForDeletion_AnonymizesAndDeletesAccountOwnedDataIdempotently()
    {
        await using var connection = await CreateSqliteConnection();
        await using var db = CreateSqliteContext(connection);
        await db.Database.EnsureCreatedAsync();
        var (user, device, badge) = await SeedDeletionData(db, UserAccountStatus.PendingDeletion, DateTime.UtcNow.AddDays(-1));
        var storage = new UsersStorage(db);
        var publisher = new Mock<IPublishEndpoint>();
        var sender = new UserInfoQueueSender(publisher.Object, new MetricsCollector());
        var handler = new ClaimAccountsForDeletionCommandHandler(storage, sender);

        var claimed = await handler.Handle(new ClaimAccountsForDeletionCommand(10), CancellationToken.None);
        var repeated = await handler.Handle(new ClaimAccountsForDeletionCommand(10), CancellationToken.None);

        claimed.UserIds.Should().ContainSingle().Which.Should().Be(user.Id);
        repeated.UserIds.Should().ContainSingle().Which.Should().Be(user.Id);
        publisher.Verify(p => p.Publish(
                It.Is<BarkFluff.Shared.Queue.Users.UserChangedName>(e => e.UserId == user.Id),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        publisher.Verify(p => p.Publish(
                It.Is<BarkFluff.Shared.Queue.Users.UserChangedAvatar>(e => e.UserId == user.Id),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        var deleted = await db.Users.SingleAsync(x => x.Id == user.Id);
        deleted.AccountStatus.Should().Be(UserAccountStatus.Deleted);
        deleted.Username.Should().StartWith("deleted-").And.NotBe("retained-name");
        deleted.Username.Should().Contain("deleted-");
        deleted.FirstName.Should().BeEmpty();
        deleted.LastName.Should().BeEmpty();
        deleted.Bio.Should().BeNull();
        deleted.ProfilePicture.Should().BeNull();
        deleted.ProfilePicturePreviewUrl.Should().BeNull();
        deleted.RestrictionReason.Should().BeNull();
        (await db.UserContacts.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.UserSettings.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.Privacies.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.UserPersonalizations.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.UserChatSettings.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.ChatFolders.CountAsync(x => x.OwnerUserId == user.Id)).Should().Be(0);
        (await db.ChatMutes.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.UserBadges.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.UserDevices.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
        (await db.DevicePrekeyBundles.CountAsync(x => x.DeviceId == device.Id)).Should().Be(0);
        (await db.OneTimePrekeys.CountAsync(x => x.DeviceId == device.Id)).Should().Be(0);
        (await db.OneTimeKyberPrekeys.CountAsync(x => x.DeviceId == device.Id)).Should().Be(0);
        (await db.Badges.CountAsync(x => x.Id == badge.Id)).Should().Be(1);
        (await storage.GetUserByEmail("remove-me@test.com")).Should().BeNull();
        (await storage.GetUserByUsername("retained-name")).Should().BeNull();

        await storage.CompleteAccountCleanup(user.Id, CancellationToken.None);
        await storage.CompleteAccountCleanup(user.Id, CancellationToken.None);
        (await storage.ClaimAccountsForDeletion(10, CancellationToken.None)).Should().BeEmpty();
        (await db.Users.SingleAsync(x => x.Id == user.Id)).CleanupCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ClaimAccountsForDeletion_CancelBeforeClaimPreservesAccountAndCancelAfterClaimFails()
    {
        await using var connection = await CreateSqliteConnection();
        await using var db = CreateSqliteContext(connection);
        await db.Database.EnsureCreatedAsync();
        var (user, _, _) = await SeedDeletionData(db, UserAccountStatus.PendingDeletion, DateTime.UtcNow.AddDays(-1));
        var storage = new UsersStorage(db);
        var sender = new UserInfoQueueSender(Mock.Of<IPublishEndpoint>(), new MetricsCollector());
        var handler = new SetAccountRestrictionCommandHandler(storage, sender);

        var cancelled = await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.CancelDeletion, "keep account"), CancellationToken.None);
        var claimed = await storage.ClaimAccountsForDeletion(10, CancellationToken.None);

        cancelled.Restriction.Status.Should().Be(BarkFluff.Proto.Users.AccountStatus.Active);
        claimed.Should().BeEmpty();
        (await db.UserContacts.CountAsync(x => x.UserId == user.Id)).Should().Be(1);

        await handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.ScheduleDeletion, "delete"), CancellationToken.None);
        await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.DeletionScheduledAt, DateTime.UtcNow.AddDays(-1)));
        (await storage.ClaimAccountsForDeletion(10, CancellationToken.None)).Should().Contain(user.Id);

        await FluentActions.Invoking(() => handler.Handle(new SetAccountRestrictionCommand(user.Id, AccountRestrictionAction.CancelDeletion, "too late"), CancellationToken.None))
            .Should().ThrowAsync<InvalidAccountRestrictionTransitionException>();
    }

    private static async Task<(SqliteConnection Connection, UsersContext Context, User User)> CreateSqliteUser()
    {
        var connection = await CreateSqliteConnection();
        var db = CreateSqliteContext(connection);
        await db.Database.EnsureCreatedAsync();
        var user = new User
        {
            Username = "restriction-user",
            FirstName = "Restriction",
            LastName = "User",
            RegistrationDate = DateTime.UtcNow,
            Contact = new UserContact { Email = "restriction@test.com" },
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (connection, db, user);
    }

    private SetAccountRestrictionCommandHandler CreateSetHandler() =>
        new(_h.UsersStorage, _h.QueueSender);

    private static async Task<SqliteConnection> CreateSqliteConnection()
    {
        var connection = new SqliteConnection($"Data Source=account-cleanup-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30");
        await connection.OpenAsync();
        connection.CreateFunction("gen_random_uuid", () => Guid.NewGuid().ToString());
        return connection;
    }

    private static UsersContext CreateSqliteContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<UsersContext>().UseSqlite(connection).Options);

    private static async Task<(User User, UserDevice Device, Badge Badge)> SeedDeletionData(
        UsersContext db, UserAccountStatus status, DateTime deletionAt)
    {
        var user = new User
        {
            Id = 7001,
            Uuid = Guid.NewGuid(),
            Username = "retained-name",
            FirstName = "Retained",
            LastName = "Name",
            RegistrationDate = DateTime.UtcNow.AddYears(-1),
            Bio = "bio",
            ProfilePicture = "avatar",
            ProfilePicturePreviewUrl = "preview",
            AccountStatus = status,
            RestrictionReason = "delete",
            RestrictedAt = DateTime.UtcNow.AddDays(-7),
            DeletionScheduledAt = deletionAt,
            Contact = new UserContact { Email = "remove-me@test.com" },
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var device = new UserDevice { Id = Guid.NewGuid(), UserId = user.Id, OriginalName = "phone", AuthorizedAt = DateTime.UtcNow };
        var badge = new Badge { Name = "badge", Description = "badge", ImageUrl = "badge.png", IsActive = true, CreatedDate = DateTime.UtcNow };
        db.UserDevices.Add(device);
        db.DevicePrekeyBundles.Add(new DevicePrekeyBundle { DeviceId = device.Id, Device = device, RegistrationId = 1, SignedPrekeyRotatedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        db.OneTimePrekeys.Add(new OneTimePrekey { Id = 1, DeviceId = device.Id, Device = device, PrekeyId = 1, CreatedAt = DateTime.UtcNow });
        db.OneTimeKyberPrekeys.Add(new OneTimeKyberPrekey { Id = 1, DeviceId = device.Id, Device = device, PrekeyId = 1, CreatedAt = DateTime.UtcNow });
        db.Badges.Add(badge);
        db.UserBadges.Add(new UserBadge { UserId = user.Id, Badge = badge, BadgeId = badge.Id, Priority = 1, AssignedDate = DateTime.UtcNow });
        db.UserSettings.Add(new BarkFluff.Users.Domain.UserSettings { UserId = user.Id });
        db.Privacies.Add(new BarkFluff.Users.Domain.Privacy { UserId = user.Id });
        db.UserPersonalizations.Add(new UserPersonalization { UserId = user.Id, ProfilePosterFileId = "poster" });
        db.UserChatSettings.Add(new UserChatSettings { UserId = user.Id, ChatId = Guid.NewGuid(), ChatBackgroundFileId = "background" });
        db.ChatFolders.Add(new ChatFolder { OwnerUserId = user.Id, FolderId = Guid.NewGuid(), FolderName = "folder" });
        db.ChatMutes.Add(new ChatMute { UserId = user.Id, ChatId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        return (user, device, badge);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
