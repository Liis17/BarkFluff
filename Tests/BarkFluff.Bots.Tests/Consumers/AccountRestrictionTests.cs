using BarkFluff.Bots.Consumers;
using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Persistence;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Users;

using Grpc.Core;

using MassTransit;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Bots.Tests.Consumers;

public class AccountRestrictionTests
{
    [Fact]
    public async Task UserAccountStatusChangedConsumer_AppliesCurrentStatusIdempotently()
    {
        await using var connection = await CreateConnection();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await context.Bots.AddAsync(new Bot { Id = 10, OwnerUserId = 42, Username = "bot" });
        await context.SaveChangesAsync();

        var bot = await context.Bots.SingleAsync();
        var registryCache = CreateRegistryCache();
        registryCache.Load([bot]);

        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.GetAccountRestrictionAsync(
                It.IsAny<GetAccountRestrictionRequest>(), null, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(Unary(new GetAccountRestrictionResponse
            {
                Restriction = new AccountRestrictionInfo { Status = AccountStatus.Blocked },
            }));

        var consumer = new UserAccountStatusChangedConsumer(
            users.Object,
            new BotsStorage(context),
            registryCache,
            Mock.Of<ILogger<UserAccountStatusChangedConsumer>>());
        var consumeContext = new Mock<ConsumeContext<UserAccountStatusChanged>>();
        consumeContext.Setup(c => c.Message).Returns(new UserAccountStatusChanged { UserId = 42, Status = (int)AccountStatus.Active });
        consumeContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(consumeContext.Object);
        await consumer.Consume(consumeContext.Object);

        Assert.True(await context.Bots.Select(item => item.OwnerRestricted).SingleAsync());
        Assert.True(registryCache.Get(10)!.OwnerRestricted);
        users.Verify(client => client.GetAccountRestrictionAsync(
            It.Is<GetAccountRestrictionRequest>(request => request.UserId == 42), null, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task BotOwnerRestrictionReconciler_BatchesOwnersAtStartupAndRefreshesCache()
    {
        await using var connection = await CreateConnection();
        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.ListByIdsAsync(
                It.IsAny<ListByIdsRequest>(), null, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns((ListByIdsRequest request, Metadata? _, DateTime? _, CancellationToken _) =>
            {
                Assert.Equal(new long[] { 42, 43 }, request.Ids.Order());
                return Unary(new ListByIdsResponse
                {
                    Users =
                    {
                        new User { Id = 42, AccountStatus = AccountStatus.Blocked },
                        new User { Id = 43, AccountStatus = AccountStatus.Active },
                    },
                });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<BotsContext>(options => options.UseSqlite(connection));
        services.AddScoped<BotsStorage>();
        services.AddSingleton(users.Object);
        await using var provider = services.BuildServiceProvider();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<BotsContext>();
            await db.Database.EnsureCreatedAsync();
            await db.Bots.AddRangeAsync(
                new Bot { Id = 10, OwnerUserId = 42, Username = "blocked-owner-bot" },
                new Bot { Id = 11, OwnerUserId = 43, Username = "active-owner-bot", OwnerRestricted = true },
                new Bot { Id = 12, OwnerUserId = null, Username = "system-bot" });
            await db.SaveChangesAsync();
        }

        var registryCache = CreateRegistryCache();
        var reconciler = new BotOwnerRestrictionReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registryCache,
            Mock.Of<ILogger<BotOwnerRestrictionReconciler>>());

        await reconciler.StartAsync(CancellationToken.None);
        await reconciler.StopAsync(CancellationToken.None);

        await using var verifyScope = provider.CreateAsyncScope();
        var persistedBots = await verifyScope.ServiceProvider.GetRequiredService<BotsContext>()
            .Bots.OrderBy(bot => bot.Id).ToListAsync();
        Assert.True(persistedBots[0].OwnerRestricted);
        Assert.False(persistedBots[1].OwnerRestricted);
        Assert.False(persistedBots[2].OwnerRestricted);
        Assert.True(registryCache.Get(10)!.OwnerRestricted);
        Assert.False(registryCache.Get(11)!.OwnerRestricted);
        Assert.Null(registryCache.Get(12)!.OwnerUserId);
        users.Verify(client => client.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null,
            It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBotsForOwner_IsIdempotentAcrossRepeatedCalls()
    {
        await using var connection = await CreateConnection();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await context.Bots.AddAsync(new Bot { Id = 10, OwnerUserId = 42, Username = "bot" });
        await context.SaveChangesAsync();

        var storage = new BotsStorage(context);
        var mediator = new Mock<MediatR.IMediator>();
        mediator.Setup(sender => sender.Send(It.IsAny<BarkFluff.Bots.Features.DeleteBot.DeleteBotCommand>(), CancellationToken.None))
            .Returns<BarkFluff.Bots.Features.DeleteBot.DeleteBotCommand, CancellationToken>(async (command, _) =>
            {
                await storage.Delete(command.BotId);
                return new BarkFluff.Proto.Bots.DeleteBotResponse();
            });
        var handler = new BarkFluff.Bots.Features.DeleteBotsForOwner.DeleteBotsForOwnerCommandHandler(storage, mediator.Object);
        var request = new BarkFluff.Bots.Features.DeleteBotsForOwner.DeleteBotsForOwnerCommand { OwnerUserId = 42 };

        await handler.Handle(request, CancellationToken.None);
        await handler.Handle(request, CancellationToken.None);

        Assert.Empty(await context.Bots.ToListAsync());
        mediator.Verify(sender => sender.Send(It.IsAny<BarkFluff.Bots.Features.DeleteBot.DeleteBotCommand>(), CancellationToken.None), Times.Once);
    }

    private static async Task<SqliteConnection> CreateConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static BotsContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<BotsContext>().UseSqlite(connection).Options;
        return new BotsContext(options);
    }

    private static BotRegistryCache CreateRegistryCache()
        => new(Mock.Of<IBus>(), Mock.Of<ILogger<BotRegistryCache>>());

    private static AsyncUnaryCall<T> Unary<T>(T response)
        => new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
