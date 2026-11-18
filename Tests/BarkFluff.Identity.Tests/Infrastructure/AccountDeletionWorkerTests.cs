using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using Grpc.Core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using StackExchange.Redis;

using Xunit;

namespace BarkFluff.Identity.Tests.Infrastructure;

public class AccountDeletionWorkerTests
{
    [Fact]
    public async Task RunLeasedCycle_DoesNotClaimWhenAnotherInstanceOwnsLease()
    {
        var redis = CreateRedis(leaseAcquired: false, out _);
        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var worker = new AccountDeletionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), redis.Object, NullLogger<AccountDeletionWorker>.Instance);

        await worker.RunLeasedCycle(CancellationToken.None);

        users.Verify(client => client.ClaimAccountsForDeletionAsync(
            It.IsAny<ClaimAccountsForDeletionRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("files")]
    [InlineData("bots")]
    public async Task RunLeasedCycle_RetriesPartialCleanupAndCompletesAfterFailure(string failedStep)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<IdentityContext>().UseSqlite(connection).Options;
        await using (var setupContext = new IdentityContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.RefreshTokens.Add(new RefreshToken
            {
                UserId = 42,
                DeviceId = "device",
                Value = "refresh",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
            });
            await setupContext.SaveChangesAsync();
        }

        if (failedStep == "identity")
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_refresh_delete BEFORE DELETE ON \"RefreshTokens\" BEGIN SELECT RAISE(FAIL, 'temporary failure'); END;";
            await command.ExecuteNonQueryAsync();
        }

        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.ClaimAccountsForDeletionAsync(
                It.IsAny<ClaimAccountsForDeletionRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() => Unary(new ClaimAccountsForDeletionResponse { UserIds = { 42 } }));
        users.Setup(client => client.CompleteAccountCleanupAsync(
                It.IsAny<CompleteAccountCleanupRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() => Unary(new CompleteAccountCleanupResponse()));

        var filesAttempts = 0;
        var files = new Mock<FilesServerApi.FilesServerApiClient>();
        files.Setup(client => client.DeleteUserProfileImagesAsync(
                It.IsAny<DeleteUserProfileImagesRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                filesAttempts++;
                if (failedStep == "files" && filesAttempts == 1)
                    throw new RpcException(new Status(StatusCode.Unavailable, "temporary failure"));
                return Unary(new DeleteUserProfileImagesResponse());
            });

        var botAttempts = 0;
        var bots = new Mock<BotsServerApi.BotsServerApiClient>();
        bots.Setup(client => client.DeleteBotsForOwnerAsync(
                It.IsAny<DeleteBotsForOwnerRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                botAttempts++;
                if (failedStep == "bots" && botAttempts == 1)
                    throw new RpcException(new Status(StatusCode.Unavailable, "temporary failure"));
                return Unary(new DeleteBotsForOwnerResponse());
            });

        var services = new ServiceCollection();
        services.AddDbContext<IdentityContext>(builder => builder.UseSqlite(connection));
        services.AddSingleton(users.Object);
        services.AddSingleton(files.Object);
        services.AddSingleton(bots.Object);
        using var provider = services.BuildServiceProvider();
        var redis = CreateRedis(leaseAcquired: true, out _);
        var worker = new AccountDeletionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), redis.Object, NullLogger<AccountDeletionWorker>.Instance);

        await worker.RunLeasedCycle(CancellationToken.None);

        users.Verify(client => client.CompleteAccountCleanupAsync(
            It.IsAny<CompleteAccountCleanupRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);

        if (failedStep == "identity")
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TRIGGER fail_refresh_delete;";
            await command.ExecuteNonQueryAsync();
        }

        await worker.RunLeasedCycle(CancellationToken.None);

        users.Verify(client => client.ClaimAccountsForDeletionAsync(
            It.IsAny<ClaimAccountsForDeletionRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        users.Verify(client => client.CompleteAccountCleanupAsync(
            It.Is<CompleteAccountCleanupRequest>(request => request.UserId == 42), null, null, It.IsAny<CancellationToken>()), Times.Once);
        files.Verify(client => client.DeleteUserProfileImagesAsync(
            It.Is<DeleteUserProfileImagesRequest>(request => request.UserId == 42), null, null, It.IsAny<CancellationToken>()),
            Times.Exactly(failedStep == "identity" ? 1 : 2));
        bots.Verify(client => client.DeleteBotsForOwnerAsync(
            It.Is<DeleteBotsForOwnerRequest>(request => request.OwnerUserId == 42), null, null, It.IsAny<CancellationToken>()),
            Times.Exactly(failedStep is "identity" or "files" ? 1 : 2));
    }

    private static Mock<IConnectionMultiplexer> CreateRedis(bool leaseAcquired, out Mock<IDatabase> database)
    {
        database = new Mock<IDatabase>();
        database.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(leaseAcquired);
        database.Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync(default(RedisResult)!);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(client => client.GetDatabase(-1, null)).Returns(database.Object);
        return redis;
    }

    private static AsyncUnaryCall<T> Unary<T>(T response)
        => new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
