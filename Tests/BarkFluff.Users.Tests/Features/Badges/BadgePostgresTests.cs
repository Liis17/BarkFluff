using BarkFluff.Users.Domain;
using BarkFluff.Users.Features.Badges;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Tests.Features.Prekeys;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace BarkFluff.Users.Tests.Features.Badges;

public class BadgePostgresTests
{
    [PrekeyPostgresFact]
    public async Task ConcurrentSelectionOfDifferentBadgesLeavesExactlyOnePrimary()
    {
        await using var database = await BadgeDatabase.Create();
        var seeded = await SeedUserWithBadgesAsync(database.Context);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstSelection = SelectAfterStart(seeded.UserId, seeded.FirstBadgeId);
        var secondSelection = SelectAfterStart(seeded.UserId, seeded.SecondBadgeId);
        start.SetResult();
        await Task.WhenAll(firstSelection, secondSelection);

        await using var verifyContext = database.CreateContext();
        var primaryBadges = await verifyContext.UserBadges.AsNoTracking()
            .Where(userBadge => userBadge.UserId == seeded.UserId && userBadge.IsPrimary)
            .ToListAsync();
        primaryBadges.Should().ContainSingle();
        new[] { seeded.FirstBadgeId, seeded.SecondBadgeId }.Should().Contain(primaryBadges[0].BadgeId);

        async Task SelectAfterStart(long userId, int badgeId)
        {
            await start.Task;
            await using var context = database.CreateContext();
            await new UsersStorage(context).SetPrimaryBadgeAsync(userId, badgeId);
        }
    }

    [PrekeyPostgresFact]
    public async Task SelectionCanSwitchClearAndRepeatIdempotently()
    {
        await using var database = await BadgeDatabase.Create();
        var seeded = await SeedUserWithBadgesAsync(database.Context);

        var first = await SelectAsync(database, seeded.UserId, seeded.FirstBadgeId);
        first!.IsPrimary.Should().BeTrue();
        (await SelectAsync(database, seeded.UserId, seeded.FirstBadgeId))!.IsPrimary.Should().BeTrue();

        var second = await SelectAsync(database, seeded.UserId, seeded.SecondBadgeId);
        second!.BadgeId.Should().Be(seeded.SecondBadgeId);
        await AssertOnlyPrimaryAsync(database, seeded.UserId, seeded.SecondBadgeId);

        (await SelectAsync(database, seeded.UserId, 0)).Should().BeNull();
        await AssertOnlyPrimaryAsync(database, seeded.UserId, null);
    }

    [PrekeyPostgresFact]
    public async Task InvalidSelectionRollsBackClearingTheExistingPrimary()
    {
        await using var database = await BadgeDatabase.Create();
        var seeded = await SeedUserWithBadgesAsync(database.Context);
        await SelectAsync(database, seeded.UserId, seeded.FirstBadgeId);

        await using var context = database.CreateContext();
        var exception = await FluentActions.Awaiting(() => new UsersStorage(context)
                .SetPrimaryBadgeAsync(seeded.UserId, 999999))
            .Should().ThrowAsync<BadgeException>();
        exception.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.NotFound);

        await AssertOnlyPrimaryAsync(database, seeded.UserId, seeded.FirstBadgeId);
    }

    [PrekeyPostgresFact]
    public async Task DeactivateThenReactivateDoesNotRestorePrimary()
    {
        await using var database = await BadgeDatabase.Create();
        var seeded = await SeedUserWithBadgesAsync(database.Context);
        await SelectAsync(database, seeded.UserId, seeded.FirstBadgeId);

        await using (var updateContext = database.CreateContext())
        {
            var storage = new UsersStorage(updateContext);
            var badge = await updateContext.Badges.SingleAsync(item => item.Id == seeded.FirstBadgeId);
            await storage.UpdateBadgeAsync(badge.Id, badge.Name, badge.Description ?? string.Empty, badge.ImageUrl, false, badge.MiniImageUrl);
            await storage.UpdateBadgeAsync(badge.Id, badge.Name, badge.Description ?? string.Empty, badge.ImageUrl, true, badge.MiniImageUrl);
        }

        await AssertOnlyPrimaryAsync(database, seeded.UserId, null);
        await using var context = database.CreateContext();
        var exception = await FluentActions.Awaiting(() => new UsersStorage(context)
                .SetPrimaryBadgeAsync(seeded.UserId, seeded.InactiveBadgeId))
            .Should().ThrowAsync<BadgeException>();
        exception.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.NotFound);
    }

    [PrekeyPostgresFact]
    public async Task UniqueViolationRetriesOnceAndCompletesSelection()
    {
        await using var database = await BadgeDatabase.Create();
        var seeded = await SeedUserWithBadgesAsync(database.Context);

        await using (var triggerContext = database.CreateContext())
        {
            await triggerContext.Database.ExecuteSqlRawAsync("""
                CREATE SEQUENCE badge_primary_retry_sequence START WITH 1;
                CREATE FUNCTION fail_first_badge_primary_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF nextval('badge_primary_retry_sequence') = 1 THEN
                        RAISE unique_violation USING CONSTRAINT = 'IX_UserBadges_UserId_Primary';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER fail_first_badge_primary_update_trigger
                BEFORE UPDATE ON "UserBadges"
                FOR EACH ROW
                WHEN (NEW."IsPrimary" = TRUE)
                EXECUTE FUNCTION fail_first_badge_primary_update();
                """);
        }

        var result = await SelectAsync(database, seeded.UserId, seeded.SecondBadgeId);
        result!.IsPrimary.Should().BeTrue();
        await AssertOnlyPrimaryAsync(database, seeded.UserId, seeded.SecondBadgeId);

        await using var verifyContext = database.CreateContext();
        await verifyContext.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)verifyContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_value FROM badge_primary_retry_sequence";
        (await command.ExecuteScalarAsync()).Should().Be(2L);
    }

    [PrekeyPostgresFact]
    public async Task RemovingOrDeletingPrimaryBadgeCascadesItsSelection()
    {
        await using var database = await BadgeDatabase.Create();
        var seeded = await SeedUserWithBadgesAsync(database.Context);
        await SelectAsync(database, seeded.UserId, seeded.FirstBadgeId);

        await using (var context = database.CreateContext())
        {
            (await new UsersStorage(context).RemoveBadgeFromUserAsync(seeded.UserId, seeded.FirstBadgeId)).Should().BeTrue();
        }
        await AssertOnlyPrimaryAsync(database, seeded.UserId, null);

        await SelectAsync(database, seeded.UserId, seeded.SecondBadgeId);
        await using (var context = database.CreateContext())
        {
            (await new UsersStorage(context).DeleteBadgeAsync(seeded.SecondBadgeId)).Should().BeTrue();
        }
        await AssertOnlyPrimaryAsync(database, seeded.UserId, null);
    }

    private static async Task<(long UserId, int FirstBadgeId, int SecondBadgeId, int InactiveBadgeId)> SeedUserWithBadgesAsync(
        UsersContext context)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Username = $"badge_{suffix}",
            FirstName = "Badge",
            LastName = "Test",
            RegistrationDate = DateTime.UtcNow,
            Contact = new UserContact { Email = $"{suffix}@test.invalid" }
        };
        var first = CreateBadge("First", isActive: true);
        var second = CreateBadge("Second", isActive: true);
        var inactive = CreateBadge("Inactive", isActive: false);

        context.Users.Add(user);
        context.Badges.AddRange(first, second, inactive);
        await context.SaveChangesAsync();
        context.UserBadges.AddRange(
            CreateUserBadge(user.Id, first.Id),
            CreateUserBadge(user.Id, second.Id),
            CreateUserBadge(user.Id, inactive.Id));
        await context.SaveChangesAsync();

        return (user.Id, first.Id, second.Id, inactive.Id);
    }

    private static Badge CreateBadge(string name, bool isActive) => new()
    {
        Name = name,
        ImageUrl = $"https://example.test/{name}.png",
        MiniImageUrl = $"https://example.test/{name}-mini.png",
        CreatedDate = DateTime.UtcNow,
        IsActive = isActive
    };

    private static UserBadge CreateUserBadge(long userId, int badgeId) => new()
    {
        UserId = userId,
        BadgeId = badgeId,
        AssignedDate = DateTime.UtcNow
    };

    private static async Task<UserBadge?> SelectAsync(BadgeDatabase database, long userId, int badgeId)
    {
        await using var context = database.CreateContext();
        return await new UsersStorage(context).SetPrimaryBadgeAsync(userId, badgeId);
    }

    private static async Task AssertOnlyPrimaryAsync(BadgeDatabase database, long userId, int? expectedBadgeId)
    {
        await using var context = database.CreateContext();
        var primaryBadges = await context.UserBadges.AsNoTracking()
            .Where(userBadge => userBadge.UserId == userId && userBadge.IsPrimary)
            .ToListAsync();

        if (expectedBadgeId is null)
        {
            primaryBadges.Should().BeEmpty();
            return;
        }

        primaryBadges.Should().ContainSingle().Which.BadgeId.Should().Be(expectedBadgeId.Value);
    }

    private sealed class BadgeDatabase : IAsyncDisposable
    {
        private readonly string _rootConnectionString;
        private readonly string _databaseName;

        public UsersContext Context { get; }

        private BadgeDatabase(string rootConnectionString, string databaseName, UsersContext context)
        {
            _rootConnectionString = rootConnectionString;
            _databaseName = databaseName;
            Context = context;
        }

        public static async Task<BadgeDatabase> Create()
        {
            var rootConnectionString = Environment.GetEnvironmentVariable("BARKFLUFF_PREKEY_POSTGRES")!;
            var databaseName = "barkfluff_badge_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(rootConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
                await command.ExecuteNonQueryAsync();
            }

            var database = new BadgeDatabase(rootConnectionString, databaseName, BuildContext(GetDatabaseConnectionString(rootConnectionString, databaseName)));
            await database.Context.Database.MigrateAsync();
            return database;
        }

        public UsersContext CreateContext()
        {
            return BuildContext(GetDatabaseConnectionString(_rootConnectionString, _databaseName));
        }

        private static string GetDatabaseConnectionString(string rootConnectionString, string databaseName)
        {
            return new NpgsqlConnectionStringBuilder(rootConnectionString)
            {
                Database = databaseName,
                Pooling = false
            }.ConnectionString;
        }

        private static UsersContext BuildContext(string connectionString)
        {
            var options = new DbContextOptionsBuilder<UsersContext>()
                .UseNpgsql(connectionString, postgres => postgres.EnableRetryOnFailure(3))
                .Options;
            return new UsersContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await using var connection = new NpgsqlConnection(_rootConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{_databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
