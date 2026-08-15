using BarkFluff.Federation.BackgroundServices;
using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Tests.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Federation.Tests.BackgroundServices;

// Janitor использует ExecuteDeleteAsync — не поддержан EF InMemory-провайдером, поэтому тесты
// гоняются на SQLite in-memory (relational, ExecuteDelete транслируется в SQL DELETE).
public class OutboxJanitorTests
{
    // SQLite не мапит string[] нативно (Npgsql — да): конвертер нужен только в тестах.
    private sealed class SqliteFederationContext : FederationContext
    {
        public SqliteFederationContext(DbContextOptions<FederationContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<KnownServer>().Property(s => s.TlsSpkiSha256).HasConversion(
                v => string.Join('\u001f', v),
                v => v.Split('\u001f', StringSplitOptions.RemoveEmptyEntries),
                new ValueComparer<string[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                    v => v.ToArray()));
        }
    }

    // In-memory SQLite живёт, пока открыто соединение — один fixture на тест.
    private sealed class SqliteFixture : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }

        public SqliteFixture(IDictionary<string, string?>? configOverrides = null)
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TestHelpers.CreateConfiguration(configOverrides));
            services.AddScoped(_ => CreateContext());
            Provider = services.BuildServiceProvider();

            using var context = CreateContext();
            context.Database.EnsureCreated();
        }

        public FederationContext CreateContext()
            => new SqliteFederationContext(
                new DbContextOptionsBuilder<FederationContext>().UseSqlite(_connection).Options);

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }

    private static OutboxJanitor CreateJanitor(SqliteFixture fixture, FakeSingleRunner? singleRunner = null)
        => new(
            fixture.Provider.GetRequiredService<IServiceScopeFactory>(),
            singleRunner ?? new FakeSingleRunner(),
            NullLogger<OutboxJanitor>.Instance);

    private static async Task SeedOutboxAsync(SqliteFixture fixture, OutboxStatus status, DateTime createdAt)
    {
        await using var context = fixture.CreateContext();
        context.Outbox.Add(new FederationOutbox
        {
            Destination = "peer.test",
            EventId = Guid.NewGuid(),
            EventType = "NewMessage",
            PayloadBytes = [1],
            CreatedAt = createdAt,
            NextAttemptAt = createdAt,
            Status = status,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedProcessedAsync(SqliteFixture fixture, DateTime receivedAt)
    {
        await using var context = fixture.CreateContext();
        context.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = Guid.NewGuid(),
            OriginServer = "peer.test",
            ReceivedAt = receivedAt,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Cleanup_DeletesDeliveredOlderThan7Days_KeepsRecentAndPending()
    {
        using var fixture = new SqliteFixture();
        var janitor = CreateJanitor(fixture);

        await SeedOutboxAsync(fixture, OutboxStatus.Delivered, DateTime.UtcNow.AddDays(-8));
        await SeedOutboxAsync(fixture, OutboxStatus.Delivered, DateTime.UtcNow.AddDays(-1));
        await SeedOutboxAsync(fixture, OutboxStatus.Pending, DateTime.UtcNow.AddDays(-30));

        await janitor.StartAsync(CancellationToken.None);
        await TestHelpers.WaitUntilAsync(
            async () =>
            {
                await using var context = fixture.CreateContext();
                return await context.Outbox.CountAsync() == 2;
            },
            "старый Delivered должен быть удалён");
        await janitor.StopAsync(CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Outbox.CountAsync(r => r.Status == OutboxStatus.Delivered)).Should().Be(1);
        (await verify.Outbox.CountAsync(r => r.Status == OutboxStatus.Pending)).Should().Be(1, "Pending не трогаем независимо от возраста");
    }

    [Fact]
    public async Task Cleanup_DeletesProcessedEventsOlderThan14Days()
    {
        using var fixture = new SqliteFixture();
        var janitor = CreateJanitor(fixture);

        await SeedProcessedAsync(fixture, DateTime.UtcNow.AddDays(-15));
        await SeedProcessedAsync(fixture, DateTime.UtcNow.AddDays(-1));

        await janitor.StartAsync(CancellationToken.None);
        await TestHelpers.WaitUntilAsync(
            async () =>
            {
                await using var context = fixture.CreateContext();
                return await context.ProcessedEvents.CountAsync() == 1;
            },
            "старый ProcessedEvent должен быть удалён");
        await janitor.StopAsync(CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.ProcessedEvents.SingleAsync()).ReceivedAt.Should().BeOnOrAfter(DateTime.UtcNow.AddDays(-2));
    }

    [Fact]
    public async Task Cleanup_RespectsConfiguredTtl()
    {
        using var fixture = new SqliteFixture(new Dictionary<string, string?>
        {
            ["Federation:OutboxDeliveredTtlHours"] = "1",
            ["Federation:ProcessedEventsTtlHours"] = "1",
        });
        var janitor = CreateJanitor(fixture);

        await SeedOutboxAsync(fixture, OutboxStatus.Delivered, DateTime.UtcNow.AddHours(-2));
        await SeedProcessedAsync(fixture, DateTime.UtcNow.AddHours(-2));

        await janitor.StartAsync(CancellationToken.None);
        await TestHelpers.WaitUntilAsync(
            async () =>
            {
                await using var context = fixture.CreateContext();
                return await context.Outbox.CountAsync() == 0
                    && await context.ProcessedEvents.CountAsync() == 0;
            },
            "записи старше часа должны быть удалены при TTL=1ч");
        await janitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cleanup_NotLeader_SkipsTick()
    {
        // Single-runner (docs/scaling/federation.md): инстанс без лидерства не чистит —
        // чистку выполняет только инстанс-лидер.
        using var fixture = new SqliteFixture();
        var janitor = CreateJanitor(fixture, new FakeSingleRunner { Leader = false });

        await SeedOutboxAsync(fixture, OutboxStatus.Delivered, DateTime.UtcNow.AddDays(-8));
        await SeedProcessedAsync(fixture, DateTime.UtcNow.AddDays(-15));

        await janitor.StartAsync(CancellationToken.None);
        await Task.Delay(200); // негативный кейс: ждём фиксированно, чистки быть не должно
        await janitor.StopAsync(CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Outbox.CountAsync()).Should().Be(1, "не-лидер не должен чистить outbox");
        (await verify.ProcessedEvents.CountAsync()).Should().Be(1, "не-лидер не должен чистить ProcessedEvents");
    }

    [Fact]
    public async Task Cleanup_EmptyTables_NoErrors()
    {
        using var fixture = new SqliteFixture();
        var janitor = CreateJanitor(fixture);

        await janitor.StartAsync(CancellationToken.None);
        await Task.Delay(200); // просто даём отработать первой итерации
        await janitor.StopAsync(CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Outbox.CountAsync()).Should().Be(0);
    }
}
