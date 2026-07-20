using System.Diagnostics;

using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Infrastructure;

// Общие хелперы юнит-тестов Federation: InMemory-БД с шарингом между контекстами,
// конфигурация, DI-провайдер по образцу Program.cs, gRPC-заглушки, ожидание фоновых циклов.
public static class TestHelpers
{
    public const string OwnServerName = "node-a.test";

    // EF InMemory: разные экземпляры контекста видят одну «базу» только через общий
    // InMemoryDatabaseRoot — без него контекст из DI и контекст сидирования в тесте изолированы.
    public sealed record TestDatabase(string Name, InMemoryDatabaseRoot Root);

    public static TestDatabase CreateDatabase() => new(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot());

    public static FederationContext CreateContext(TestDatabase db)
        => new(new DbContextOptionsBuilder<FederationContext>()
            .UseInMemoryDatabase(db.Name, db.Root)
            .Options);

    public static FederationContext CreateContext() => CreateContext(CreateDatabase());

    public static IConfiguration CreateConfiguration(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Federation:ServerName"] = OwnServerName,
            ["Federation:Enabled"] = "true",
        };

        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
                values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static SigningKeyService CreateSigningKeyService(FederationContext context, IConfiguration? configuration = null)
        => new(context, configuration ?? CreateConfiguration(), NullLogger<SigningKeyService>.Instance);

    // Генерирует (или возвращает существующий) активный signing-ключ в InMemory-БД.
    public static async Task<FederationSigningKey> EnsureActiveKeyAsync(FederationContext context)
    {
        var service = CreateSigningKeyService(context);
        await service.EnsureActiveKeyAsync();
        return await service.GetActiveKeyAsync();
    }

    // DI по минимальному образцу Program.cs: scoped FederationContext/SigningKeyService/ServerResolver
    // поверх общей TestDatabase. Discovery-фейки без сети, если реальные не переданы.
    public static ServiceProvider CreateProvider(
        TestDatabase db,
        IConfiguration? configuration = null,
        IWellKnownClient? wellKnownClient = null,
        INavigatorClient? navigatorClient = null,
        ServernameValidator? servernameValidator = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration ?? CreateConfiguration());
        services.AddDbContext<FederationContext>(o => o.UseInMemoryDatabase(db.Name, db.Root));
        services.AddScoped<SigningKeyService>();
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<FederationSwitch>();
        services.AddSingleton(wellKnownClient ?? new FakeWellKnownClient());
        services.AddSingleton(navigatorClient ?? new FakeNavigatorClient());
        services.AddSingleton(servernameValidator ?? new LoopbackServernameValidator());
        services.AddSingleton<ActiveSigningKeyCache>();
        services.AddSingleton<S2SChannelFactory>();
        services.AddSingleton<IS2SChannelInvalidator>(sp => sp.GetRequiredService<S2SChannelFactory>());
        services.AddScoped<ServerResolver>();
        services.AddSingleton<DiscoveryTriggerRateLimiter>();
        return services.BuildServiceProvider();
    }

    public static AsyncUnaryCall<T> UnaryCall<T>(T response)
        => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    // ServerCallContext с UserState["xfed-origin"] — то, что XFedServerInterceptor кладёт после проверки.
    public static ServerCallContext CreateCallContext(string? xfedOrigin = null)
    {
        var context = new Mock<ServerCallContext>();
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var state = new Dictionary<object, object>();
        if (xfedOrigin != null)
            state["xfed-origin"] = xfedOrigin;
        context.Setup(c => c.UserState).Returns(state);

        return context.Object;
    }

    // Фоновые сервисы (диспетчер/janitor/refresh) выполняют первую итерацию сразу при StartAsync —
    // ждём наблюдаемый эффект в БД вместо фиксированного Sleep.
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string because, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (await condition())
                return;
            await Task.Delay(25);
        }

        (await condition()).Should().BeTrue(because);
    }
}
