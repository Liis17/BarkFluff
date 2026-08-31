using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Host;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Onliner;
using BarkFluff.Proto.Users;

using Grpc.Net.Client;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Moq;

namespace BarkFluff.Federation.Tests.Infrastructure;

// In-proc хост FederationS2SApi (TestServer) без внешних зависимостей (Postgres/Settings-сервис) —
// см. критерий готовности этапа 1.3: "через in-proc хост (WebApplicationFactory или два Kestrel)".
public sealed class FederationTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    public GrpcChannel Channel { get; }
    public string OwnServerName { get; }

    private FederationTestHost(IHost host, GrpcChannel channel, string ownServerName)
    {
        _host = host;
        Channel = channel;
        OwnServerName = ownServerName;
    }

    public static async Task<FederationTestHost> CreateAsync(
        string ownServerName = "node-a.test",
        int signatureWindowSeconds = 300,
        bool enabled = true)
    {
        var dbName = Guid.NewGuid().ToString();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();

                webHost.ConfigureAppConfiguration(cfg =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Federation:ServerName"] = ownServerName,
                        ["Federation:Enabled"] = enabled ? "true" : "false",
                        ["Federation:SignatureWindowSeconds"] = signatureWindowSeconds.ToString(),
                    });
                });

                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();

                    services.AddGrpc(options =>
                    {
                        options.Interceptors.Add<ServerExceptionInterceptor>();
                    }).AddServiceOptions<FederationS2SApiService>(options =>
                    {
                        options.Interceptors.Add<XFedServerInterceptor>();
                    });

                    services.AddDbContext<FederationContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddScoped<SigningKeyService>();
                    services.AddSingleton<FederationSwitch>();
                    services.AddSingleton<MetricsCollector>();

                    // Discovery-на-лету (1.4) не тестируется здесь отдельно (см. ServerResolverTests) —
                    // фейки без сети, всегда "не нашли", чтобы XFedServerInterceptor честно падал
                    // на "неизвестный ключ" вместо реального DNS/HTTP.
                    services.AddSingleton<IWellKnownClient>(new FakeWellKnownClient());
                    services.AddSingleton<INavigatorClient>(new FakeNavigatorClient());
                    services.AddSingleton(Mock.Of<IS2SChannelInvalidator>());
                    services.AddScoped<ServerResolver>();
                    services.AddSingleton<IDiscoveryTriggerRateLimiter>(new FakeDiscoveryTriggerRateLimiter());

                    // Users-клиент для FederationS2SApiService.GetUserProfile (этап 2.1) — в интеграционных
                    // тестах S2S Ping/GetServerKeys он не вызывается, но активация сервиса требует регистрации.
                    var usersClientMock = new Mock<UsersServerApi.UsersServerApiClient>();
                    services.AddSingleton(usersClientMock.Object);

                    // Messages-клиент для FederationS2SApiService (этап 2.3, ImportFederatedChat/Message) —
                    // те же рассуждения: эти тесты S2S Ping/GetServerKeys/DeliverEvents-delivery не зовут
                    // импорт-RPC (для них есть отдельные юнит-тесты), но активация требует регистрации.
                    var messagesClientMock = new Mock<MessagesServerApi.MessagesServerApiClient>();
                    services.AddSingleton(messagesClientMock.Object);

                    // Квота ChatCreated (этап 2.5) — фейк без Redis, всегда пропускает.
                    services.AddSingleton<IChatCreatedQuotaLimiter>(new FakeChatCreatedQuotaLimiter());

                    // Presence-мост (этап 4.3) — те же рассуждения: SubscribePresence в этих
                    // тестах не зовётся, но активация FederationS2SApiService требует регистрации.
                    services.AddSingleton(Mock.Of<OnlinerServerApi.OnlinerServerApiClient>());
                    services.AddSingleton<PresenceOptions>();
                    services.AddSingleton<IncomingPresenceRegistry>();

                    // Скачивание federated-файлов (этап 3.2) — те же рассуждения: FetchFile
                    // в этих тестах не зовётся, но активация сервиса требует регистрации.
                    services.AddSingleton<IFetchFileRateLimiter>(new FakeFetchFileRateLimiter());
                    services.AddSingleton(Mock.Of<BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient>());

                    // Typing-мост (этап 4.4) — лимитер без Redis, кеш валидации in-memory.
                    services.AddSingleton<ITypingRateLimiter>(new FakeTypingRateLimiter());
                    services.AddSingleton<TypingValidationCache>();
                });

                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseMiddleware<XFedRawBytesMiddleware>();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<FederationS2SApiService>();
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        var server = host.GetTestServer();

        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = server.CreateHandler(),
        });

        return new FederationTestHost(host, channel, ownServerName);
    }

    public FederationS2SApi.FederationS2SApiClient CreateClient() => new(Channel);

    // Сидирует пира (KnownServers + KnownServerKeys) — вручную, как на стенде 1.3/1.4.
    public async Task SeedPeerAsync(string serverName, byte[] publicKey, string keyId = "ed25519:1", KnownServerStatus status = KnownServerStatus.Active)
    {
        using var scope = _host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FederationContext>();

        context.KnownServers.Add(new KnownServer
        {
            ServerName = serverName,
            FederationEndpoint = "http://localhost",
            TlsSpkiSha256 = [],
            Source = KnownServerSource.Manual,
            Status = status,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            ProtocolVersion = 1,
        });

        context.KnownServerKeys.Add(new KnownServerKey
        {
            ServerName = serverName,
            KeyId = keyId,
            PublicKey = publicKey,
        });

        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Channel.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
