using System.Net;

using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.Federation.Tests.Services;

// P1-10 (docs/rearch/problem/phase-1-open-problems.md): S2SChannelFactory обязан валидировать
// discovery-controlled endpoint (схема + резолв в допустимый IP) ДО построения канала — иначе
// вредоносный well-known/Navigator-документ уводит S2S во внутреннюю сеть. Резолв DNS в unit-тестах
// не делаем: ResolveAndValidateAsync переопределён фейком, представляющим результат резолва.
public class S2SChannelFactoryAntiSsrfTests
{
    private sealed class FakeValidator : ServernameValidator
    {
        private readonly IPAddress? _result;
        public FakeValidator(IPAddress? result) => _result = result;

        public override Task<IPAddress?> ResolveAndValidateAsync(string hostname, bool isManual, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private static ServiceProvider BuildProvider(ServernameValidator validator)
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<FederationContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<SigningKeyService>();
        services.AddSingleton<ActiveSigningKeyCache>();
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton(validator);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<S2SChannelFactory>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedPeerAsync(ServiceProvider sp, string endpoint, KnownServerSource source)
    {
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<FederationContext>();
        ctx.KnownServers.Add(new KnownServer
        {
            ServerName = "node-b.test",
            FederationEndpoint = endpoint,
            TlsSpkiSha256 = [],
            Source = source,
            Status = KnownServerStatus.Active,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            ProtocolVersion = 1,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetInvoker_EndpointResolvesToRejectedIp_Throws()
    {
        await using var sp = BuildProvider(new FakeValidator(null)); // резолв отверг адрес (приватный/loopback/rebind)
        await SeedPeerAsync(sp, "https://node-b.test:7030", KnownServerSource.WellKnown);
        var factory = sp.GetRequiredService<S2SChannelFactory>();

        var act = async () => await factory.GetInvokerAsync("node-b.test");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetInvoker_DisallowedScheme_Throws()
    {
        // http:// для не-manual пира запрещено — отклоняется до резолва.
        await using var sp = BuildProvider(new FakeValidator(IPAddress.Parse("8.8.8.8")));
        await SeedPeerAsync(sp, "http://node-b.test:7030", KnownServerSource.WellKnown);
        var factory = sp.GetRequiredService<S2SChannelFactory>();

        var act = async () => await factory.GetInvokerAsync("node-b.test");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetInvoker_ValidPublicEndpoint_BuildsInvoker()
    {
        await using var sp = BuildProvider(new FakeValidator(IPAddress.Parse("8.8.8.8")));
        await SeedPeerAsync(sp, "https://node-b.test:7030", KnownServerSource.WellKnown);
        var factory = sp.GetRequiredService<S2SChannelFactory>();

        var invoker = await factory.GetInvokerAsync("node-b.test");

        invoker.Should().NotBeNull();
    }
}
