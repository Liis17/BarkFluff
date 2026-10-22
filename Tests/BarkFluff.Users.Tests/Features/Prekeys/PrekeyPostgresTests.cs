using System.Security.Claims;

using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Domain;
using BarkFluff.Users.Features.Prekeys.GetOwnPrekeyStatus;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;

using FluentAssertions;

using Grpc.Core;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Moq;

using Npgsql;

namespace BarkFluff.Users.Tests.Features.Prekeys;

public class PrekeyPostgresTests
{
    [PrekeyPostgresFact]
    public async Task ConcurrentFetchesConsumeDistinctKeysAndThenUseLastResort()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        await RegisterAsync(
            new PrekeyStorage(database.Context), device.Id, user.Id, 1, 1,
            [(10, EcKey(10)), (11, EcKey(11))],
            [KyberKey(20), KyberKey(21)],
            KyberKey(30));

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = FetchAfterStart();
        var second = FetchAfterStart();
        start.SetResult();
        var fetched = await Task.WhenAll(first, second);

        fetched.Select(result => result.EcPrekey!.PrekeyId).Should().OnlyHaveUniqueItems();
        fetched.Select(result => result.KyberPrekey!.PrekeyId).Should().OnlyHaveUniqueItems();
        fetched.All(result => !result.KyberPrekeyIsLastResort).Should().BeTrue();
        fetched.Select(result => result.RemainingEcPrekeys).Order().Should().Equal(0, 1);
        fetched.Select(result => result.RemainingKyberPrekeys).Order().Should().Equal(0, 1);

        await using (var statusContext = database.CreateContext())
        {
            var finalStatus = await new PrekeyStorage(statusContext).GetOwnPrekeyStatusAsync(device.Id, user.Id);
            finalStatus.RemainingEcPrekeys.Should().Be(0);
            finalStatus.RemainingKyberPrekeys.Should().Be(0);
        }

        await using var fallbackContext = database.CreateContext();
        var fallback = await new PrekeyStorage(fallbackContext).FetchBundleAsync(user.Id, device.Id, requirePqxdh: true);
        fallback.Should().NotBeNull();
        fallback!.KyberPrekey!.PrekeyId.Should().Be(30);
        fallback.KyberPrekeyIsLastResort.Should().BeTrue();

        async Task<FetchedPrekeyBundle> FetchAfterStart()
        {
            await start.Task;
            await using var context = database.CreateContext();
            return (await new PrekeyStorage(context).FetchBundleAsync(user.Id, device.Id, requirePqxdh: true))!;
        }
    }

    [PrekeyPostgresFact]
    public async Task PqRequiredFetchDoesNotConsumeLegacyBundle()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        await RegisterAsync(
            new PrekeyStorage(database.Context), device.Id, user.Id, 1, 1,
            [(10, EcKey(10))], [], null);
        await using var fetchContext = database.CreateContext();
        var storage = new PrekeyStorage(fetchContext);

        var error = await FluentActions.Awaiting(() => storage.FetchBundleAsync(user.Id, device.Id, requirePqxdh: true))
            .Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);

        var legacyFetch = await storage.FetchBundleAsync(user.Id, device.Id);
        legacyFetch!.EcPrekey!.PrekeyId.Should().Be(10);
        legacyFetch.RemainingEcPrekeys.Should().Be(0);
    }

    [PrekeyPostgresFact]
    public async Task ConsumedKeyUploadsAreIdempotentAndConflictingPayloadsFail()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        var ec = EcKey(10);
        var kyber = KyberKey(20);
        await RegisterAsync(new PrekeyStorage(database.Context), device.Id, user.Id, 1, 1, [(10, ec)], [kyber], KyberKey(30));

        await using var context = database.CreateContext();
        var storage = new PrekeyStorage(context);
        var fetched = await storage.FetchBundleAsync(user.Id, device.Id, requirePqxdh: true);
        fetched!.EcPrekey.Should().NotBeNull();
        fetched.KyberPrekey!.PrekeyId.Should().Be(20);

        var reuploaded = await storage.ReplenishOneTimePrekeysAsync(
            device.Id, user.Id, [(10, ec)], [kyber]);
        reuploaded.Should().Be(new PrekeyPoolCounts(0, 0));

        var conflict = await FluentActions.Awaiting(() => storage.ReplenishOneTimePrekeysAsync(
                device.Id, user.Id, [(10, EcKey(99))], [kyber]))
            .Should().ThrowAsync<PrekeyException>();
        conflict.Which.StatusCode.Should().Be(StatusCode.AlreadyExists);

        var kyberConflict = await FluentActions.Awaiting(() => storage.ReplenishOneTimePrekeysAsync(
                device.Id, user.Id, [], [KyberKey(20, 99)]))
            .Should().ThrowAsync<PrekeyException>();
        kyberConflict.Which.StatusCode.Should().Be(StatusCode.AlreadyExists);

        (await context.OneTimePrekeys.SingleAsync(prekey => prekey.DeviceId == device.Id && prekey.PrekeyId == 10))
            .ConsumedAt.Should().NotBeNull();
        (await context.OneTimeKyberPrekeys.SingleAsync(prekey => prekey.DeviceId == device.Id && prekey.PrekeyId == 20))
            .ConsumedAt.Should().NotBeNull();
    }

    [PrekeyPostgresFact]
    public async Task RegistrationReplacementInvalidatesBothPoolsAndLegacyRetryPreservesPqState()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        var storage = new PrekeyStorage(database.Context);
        await RegisterAsync(storage, device.Id, user.Id, 1, 1, [(10, EcKey(10))], [KyberKey(20)], KyberKey(30));

        await RegisterAsync(storage, device.Id, user.Id, 1, 1, [], [], null);
        var preserved = await storage.GetOwnPrekeyStatusAsync(device.Id, user.Id);
        preserved.Bundle!.LastResortKyberPrekeyId.Should().Be(30);
        preserved.RemainingEcPrekeys.Should().Be(1);
        preserved.RemainingKyberPrekeys.Should().Be(1);

        var incompleteReplacement = await FluentActions.Awaiting(() => RegisterAsync(
                storage, device.Id, user.Id, 2, 2, [(40, EcKey(40))], [], null))
            .Should().ThrowAsync<PrekeyException>();
        incompleteReplacement.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);

        await RegisterAsync(storage, device.Id, user.Id, 2, 2, [(40, EcKey(40))], [KyberKey(50)], KyberKey(60));
        var replaced = await storage.GetOwnPrekeyStatusAsync(device.Id, user.Id);
        replaced.Bundle!.IdentityPubkey.Should().Equal(EcKey(2));
        replaced.Bundle.LastResortKyberPrekeyId.Should().Be(60);
        replaced.RemainingEcPrekeys.Should().Be(1);
        replaced.RemainingKyberPrekeys.Should().Be(1);

        (await database.Context.OneTimePrekeys.AnyAsync(prekey => prekey.DeviceId == device.Id && prekey.PrekeyId == 10)).Should().BeFalse();
        (await database.Context.OneTimeKyberPrekeys.AnyAsync(prekey => prekey.DeviceId == device.Id && prekey.PrekeyId == 20)).Should().BeFalse();
    }

    [PrekeyPostgresFact]
    public async Task ReplacementCanReuseConsumedIdsWithNewKeyMaterialInBothPools()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        var storage = new PrekeyStorage(database.Context);
        await RegisterAsync(storage, device.Id, user.Id, 1, 1, [(10, EcKey(10))], [KyberKey(20)], KyberKey(30));
        await storage.FetchBundleAsync(user.Id, device.Id, requirePqxdh: true);

        await RegisterAsync(storage, device.Id, user.Id, 2, 2,
            [(10, EcKey(11))], [new KyberPrekeyInput(20, KyberKey(21).PublicKey, Signature(21))], KyberKey(30));
        var fetched = await storage.FetchBundleAsync(user.Id, device.Id, requirePqxdh: true);
        fetched!.Bundle.IdentityPubkey.Should().Equal(EcKey(2));
        fetched.EcPrekey!.PrekeyId.Should().Be(10);
        fetched.EcPrekey.PublicKey.Should().Equal(EcKey(11));
        fetched.KyberPrekey!.PrekeyId.Should().Be(20);
        fetched.KyberPrekey.PublicKey.Should().Equal(KyberKey(21).PublicKey);
        var exhausted = await storage.GetOwnPrekeyStatusAsync(device.Id, user.Id);
        exhausted.RemainingEcPrekeys.Should().Be(0);
        exhausted.RemainingKyberPrekeys.Should().Be(0);
    }

    [PrekeyPostgresFact]
    public async Task FetchRacingWithRotationReturnsOneCoherentKeySet()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        await RegisterAsync(new PrekeyStorage(database.Context), device.Id, user.Id, 1, 1, [], [], KyberKey(30));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var fetch = FetchAfterStart();
        var rotate = RotateAfterStart();
        start.SetResult();
        await Task.WhenAll(fetch, rotate);

        var result = await fetch;
        var ecId = (uint)result.Bundle.SignedPrekeyId;
        var kyberId = result.KyberPrekey!.PrekeyId;
        ((ecId == 101 && kyberId == 30) || (ecId == 2 && kyberId == 200)).Should().BeTrue();

        async Task<FetchedPrekeyBundle> FetchAfterStart()
        {
            await start.Task;
            await using var context = database.CreateContext();
            return (await new PrekeyStorage(context).FetchBundleAsync(user.Id, device.Id, requirePqxdh: true))!;
        }

        async Task RotateAfterStart()
        {
            await start.Task;
            await using var context = database.CreateContext();
            await new PrekeyStorage(context).RotateSignedPrekeyAsync(
                device.Id, user.Id, (2, EcKey(2), Signature(2)), KyberKey(200));
        }
    }

    [PrekeyPostgresFact]
    public async Task FetchRacingWithIdentityReplacementReturnsOneRegistrationEpoch()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        await RegisterAsync(new PrekeyStorage(database.Context), device.Id, user.Id, 1, 1, [(10, EcKey(10))], [KyberKey(20)], KyberKey(30));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var fetch = FetchAfterStart();
        var replace = ReplaceAfterStart();
        start.SetResult();
        await Task.WhenAll(fetch, replace);

        var result = await fetch;
        var selectedKyberId = result.KyberPrekey!.PrekeyId;
        if (result.Bundle.IdentityPubkey.SequenceEqual(EcKey(1)))
        {
            selectedKyberId.Should().Be(20);
        }
        else
        {
            result.Bundle.IdentityPubkey.Should().Equal(EcKey(2));
            selectedKyberId.Should().Be(50);
        }

        async Task<FetchedPrekeyBundle> FetchAfterStart()
        {
            await start.Task;
            await using var context = database.CreateContext();
            return (await new PrekeyStorage(context).FetchBundleAsync(user.Id, device.Id, requirePqxdh: true))!;
        }

        async Task ReplaceAfterStart()
        {
            await start.Task;
            await using var context = database.CreateContext();
            await RegisterAsync(new PrekeyStorage(context), device.Id, user.Id, 2, 2, [(40, EcKey(40))], [KyberKey(50)], KyberKey(60));
        }
    }

    [PrekeyPostgresFact]
    public async Task OwnStatusIsNonconsumingDeviceScopedAndDeviceDeleteCascadesTombstones()
    {
        await using var database = await PrekeyDatabase.Create();
        var (user, device) = await SeedDeviceAsync(database.Context);
        var (otherUser, _) = await SeedDeviceAsync(database.Context);
        await RegisterAsync(new PrekeyStorage(database.Context), device.Id, user.Id, 7, 1, [(10, EcKey(10))], [KyberKey(20)], KyberKey(30));

        var handler = new GetOwnPrekeyStatusQueryHandler(
            new PrekeyStorage(database.CreateContext()), CreateUserContext(user.Id, device.Id));
        var first = await handler.Handle(new GetOwnPrekeyStatusQuery(), CancellationToken.None);
        var second = await handler.Handle(new GetOwnPrekeyStatusQuery(), CancellationToken.None);
        first.HasBundle.Should().BeTrue();
        first.SupportsPqxdh.Should().BeTrue();
        first.IdentityPubkey.ToByteArray().Should().Equal(EcKey(1));
        first.RegistrationId.Should().Be(7);
        first.LastResortKyberPrekey.PrekeyId.Should().Be(30);
        first.RemainingOneTimePrekeys.Should().Be(1);
        first.RemainingOneTimeKyberPrekeys.Should().Be(1);
        second.RemainingOneTimePrekeys.Should().Be(1);
        second.RemainingOneTimeKyberPrekeys.Should().Be(1);

        var wrongOwnerHandler = new GetOwnPrekeyStatusQueryHandler(
            new PrekeyStorage(database.CreateContext()), CreateUserContext(otherUser.Id, device.Id));
        var wrongOwner = await FluentActions.Awaiting(() => wrongOwnerHandler.Handle(new GetOwnPrekeyStatusQuery(), CancellationToken.None))
            .Should().ThrowAsync<PrekeyException>();
        wrongOwner.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);

        await using (var fetchContext = database.CreateContext())
        {
            await new PrekeyStorage(fetchContext).FetchBundleAsync(user.Id, device.Id, requirePqxdh: true);
        }
        (await database.Context.OneTimePrekeys.CountAsync(prekey => prekey.DeviceId == device.Id && prekey.ConsumedAt != null)).Should().Be(1);
        (await database.Context.OneTimeKyberPrekeys.CountAsync(prekey => prekey.DeviceId == device.Id && prekey.ConsumedAt != null)).Should().Be(1);

        database.Context.UserDevices.Remove(device);
        await database.Context.SaveChangesAsync();
        (await database.Context.DevicePrekeyBundles.CountAsync(bundle => bundle.DeviceId == device.Id)).Should().Be(0);
        (await database.Context.OneTimePrekeys.CountAsync(prekey => prekey.DeviceId == device.Id)).Should().Be(0);
        (await database.Context.OneTimeKyberPrekeys.CountAsync(prekey => prekey.DeviceId == device.Id)).Should().Be(0);
    }

    [PrekeyPostgresFact]
    public async Task LegacySchemaUpgradePreservesEcBundleAndCreatesNullablePqState()
    {
        await using var database = await PrekeyDatabase.Create("20260802000000_AddDevicePushPlatform");
        var (user, device) = await SeedDeviceAsync(database.Context);
        var identity = EcKey(1);
        var ec = EcKey(2);
        var signature = Signature(3);
        await database.Context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""DevicePrekeyBundles""
                (""DeviceId"", ""RegistrationId"", ""IdentityPubkey"", ""SignedPrekeyId"", ""SignedPrekeyPublic"", ""SignedPrekeySignature"", ""SignedPrekeyRotatedAt"", ""CreatedAt"")
            VALUES ({device.Id}, {7L}, {identity}, {8L}, {ec}, {signature}, {DateTime.UtcNow}, {DateTime.UtcNow})");
        await database.Context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""OneTimePrekeys"" (""DeviceId"", ""PrekeyId"", ""PublicKey"", ""CreatedAt"")
            VALUES ({device.Id}, {9L}, {EcKey(9)}, {DateTime.UtcNow})");

        await database.Context.Database.MigrateAsync();
        database.Context.ChangeTracker.Clear();
        var bundle = await database.Context.DevicePrekeyBundles.SingleAsync(item => item.DeviceId == device.Id);
        bundle.IdentityPubkey.Should().Equal(identity);
        bundle.LastResortKyberPrekeyId.Should().BeNull();
        var prekey = await database.Context.OneTimePrekeys.SingleAsync(item => item.DeviceId == device.Id);
        prekey.ConsumedAt.Should().BeNull();

        var status = await new PrekeyStorage(database.Context).GetOwnPrekeyStatusAsync(device.Id, user.Id);
        status.Bundle.Should().NotBeNull();
        status.RemainingEcPrekeys.Should().Be(1);
        status.RemainingKyberPrekeys.Should().Be(0);
    }

    private static async Task RegisterAsync(
        PrekeyStorage storage,
        Guid deviceId,
        long userId,
        long registrationId,
        byte identityMarker,
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> ecPrekeys,
        IReadOnlyList<KyberPrekeyInput> kyberPrekeys,
        KyberPrekeyInput? lastResort)
    {
        await storage.RegisterBundleAsync(
            deviceId,
            userId,
            registrationId,
            EcKey(identityMarker),
            100 + identityMarker,
            EcKey((byte)(100 + identityMarker)),
            Signature((byte)(100 + identityMarker)),
            ecPrekeys,
            lastResort,
            kyberPrekeys);
    }

    private static async Task<(User User, UserDevice Device)> SeedDeviceAsync(UsersContext context)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Username = $"prekey_{suffix}",
            FirstName = "Prekey",
            LastName = "Test",
            RegistrationDate = DateTime.UtcNow,
            Contact = new UserContact { Email = $"{suffix}@test.invalid" },
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var device = new UserDevice
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OriginalName = "Prekey Test Device",
            AuthorizedAt = DateTime.UtcNow,
        };
        context.UserDevices.Add(device);
        await context.SaveChangesAsync();
        return (user, device);
    }

    private static UserContext CreateUserContext(long userId, Guid? deviceId)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, TokenType.User.ToString()),
        };
        if (deviceId != null)
        {
            claims.Add(new Claim(IdentityClaims.DeviceId, deviceId.Value.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(item => item.HttpContext).Returns(new DefaultHttpContext { User = principal });
        return new UserContext(accessor.Object);
    }

    private static byte[] EcKey(byte marker) => Enumerable.Repeat(marker, 33).ToArray();

    private static byte[] Signature(byte marker) => Enumerable.Repeat(marker, 64).ToArray();

    private static KyberPrekeyInput KyberKey(long id, byte? marker = null)
    {
        var keyMarker = marker ?? (byte)id;
        return new KyberPrekeyInput(id, Enumerable.Repeat(keyMarker, 1586).ToArray(), Signature(keyMarker));
    }

    private sealed class PrekeyDatabase : IAsyncDisposable
    {
        private readonly string _rootConnectionString;
        private readonly string _databaseName;

        public UsersContext Context { get; }

        private PrekeyDatabase(string rootConnectionString, string databaseName, UsersContext context)
        {
            _rootConnectionString = rootConnectionString;
            _databaseName = databaseName;
            Context = context;
        }

        public static async Task<PrekeyDatabase> Create(string? targetMigration = null)
        {
            var root = Environment.GetEnvironmentVariable("BARKFLUFF_PREKEY_POSTGRES")!;
            var name = "barkfluff_prekey_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(root))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
                await command.ExecuteNonQueryAsync();
            }

            var builder = new NpgsqlConnectionStringBuilder(root)
            {
                Database = name,
                Pooling = false,
            };
            var context = BuildContext(builder.ConnectionString);
            if (targetMigration == null)
            {
                await context.Database.MigrateAsync();
            }
            else
            {
                await context.Database.MigrateAsync(targetMigration);
            }

            return new PrekeyDatabase(root, name, context);
        }

        public UsersContext CreateContext(string? connectionString = null)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString ?? GetContextConnectionString())
            {
                Pooling = false,
            };
            return BuildContext(builder.ConnectionString);
        }

        private string GetContextConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(_rootConnectionString)
            {
                Database = _databaseName,
                Pooling = false,
            };
            return builder.ConnectionString;
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

public sealed class PrekeyPostgresFactAttribute : FactAttribute
{
    public PrekeyPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BARKFLUFF_PREKEY_POSTGRES")))
        {
            Skip = "BARKFLUFF_PREKEY_POSTGRES must point to a disposable PostgreSQL instance with CREATE DATABASE access.";
        }
    }
}
