using System.Net;

using BarkFluff.Federation.Services;
using BarkFluff.Proto.Federation;

using Grpc.Core;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BarkFluff.Federation.Tests.Infrastructure;

// DNS-независимый валидатор: любой хост «резолвится» в 127.0.0.1, поэтому S2SChannelFactory
// строит loopback-канал к Kestrel из LoopbackS2SServer. Статический синтаксис
// (TryNormalizeSyntax) не подменяется — servername в тестах всё равно должен быть
// валидным DNS-именем (peer.test и т.п.).
public sealed class LoopbackServernameValidator : ServernameValidator
{
    public override Task<IPAddress?> ResolveAndValidateAsync(string hostname, bool isManual, CancellationToken ct = default)
        => Task.FromResult<IPAddress?>(IPAddress.Loopback);
}

// Конфигурируемый S2S-пир на реальном Kestrel (h2c, 127.0.0.1:0). Даёт честный прогон
// OutboxDispatcher/FederationInternalApiService → S2SChannelFactory → gRPC → FederationS2SApiBase
// без Docker и внешнего DNS.
public sealed class LoopbackS2SServer : IAsyncDisposable
{
    private readonly IHost _host;

    public int Port { get; }
    public string HostName { get; }
    public string Endpoint { get; }

    private LoopbackS2SServer(IHost host, int port, string hostName)
    {
        _host = host;
        Port = port;
        HostName = hostName;
        Endpoint = $"http://{hostName}:{port}";
    }

    public static async Task<LoopbackS2SServer> StartAsync(StubS2SApi stub, string hostName = "peer.test")
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseKestrel();
            webHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));

            webHost.ConfigureServices(services =>
            {
                services.AddGrpc();
                services.AddRouting();
                services.AddSingleton(stub);
            });

            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGrpcService<StubS2SApi>());
            });
        });

        var host = await hostBuilder.StartAsync();
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new LoopbackS2SServer(host, new Uri(address).Port, hostName);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

public sealed class StubS2SApi : FederationS2SApi.FederationS2SApiBase
{
    public Func<DeliverEventsRequest, DeliverEventsResponse>? OnDeliverEvents { get; set; }
    public Func<GetUserProfileRequest, GetUserProfileResponse>? OnGetUserProfile { get; set; }

    public override Task<DeliverEventsResponse> DeliverEvents(DeliverEventsRequest request, ServerCallContext context)
        => Task.FromResult(OnDeliverEvents?.Invoke(request) ?? new DeliverEventsResponse());

    public override Task<GetUserProfileResponse> GetUserProfile(GetUserProfileRequest request, ServerCallContext context)
        => Task.FromResult(OnGetUserProfile?.Invoke(request) ?? new GetUserProfileResponse { Found = false });
}
