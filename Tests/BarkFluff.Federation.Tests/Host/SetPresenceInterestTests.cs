using BarkFluff.Federation.Host;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.FederationInternal;

using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Federation.Tests.Host;

/// <summary>
/// Регистрация интереса к remote-presence от инстансов Onliner (этап 4.3).
/// </summary>
public class SetPresenceInterestTests
{
    private sealed record Harness(FederationInternalApiService Service, PresenceInterestRegistry Registry);

    private static Harness CreateHarness(int maxSubscriptionSize = 500)
    {
        var configuration = TestHelpers.CreateConfiguration(new Dictionary<string, string?>
        {
            ["Federation:MaxPresenceSubscriptionSize"] = maxSubscriptionSize.ToString(),
        });

        var db = TestHelpers.CreateDatabase();
        var provider = TestHelpers.CreateProvider(db, configuration);
        var context = TestHelpers.CreateContext(db);
        var options = new PresenceOptions(configuration);
        var registry = new PresenceInterestRegistry(options);

        var signing = TestHelpers.CreateSigningKeyService(context, configuration);
        var keyCache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());

        var service = new FederationInternalApiService(
            context,
            configuration,
            signing,
            new WellKnownDocumentService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                configuration),
            keyCache,
            provider.GetRequiredService<ServerResolver>(),
            provider.GetRequiredService<S2SChannelFactory>(),
            new OutboxWriter(context, signing, configuration, new MetricsCollector()),
            registry,
            options);

        return new Harness(service, registry);
    }

    private static SetPresenceInterestRequest Request(string instanceId, params Guid[] uuids)
    {
        var request = new SetPresenceInterestRequest { InstanceId = instanceId };
        request.UserUuids.AddRange(uuids.Select(u => u.ToString()));
        return request;
    }

    [Fact]
    public async Task SetPresenceInterest_StoresSetForInstance()
    {
        var harness = CreateHarness();
        var uuid = Guid.NewGuid();

        var response = await harness.Service.SetPresenceInterest(
            Request("instance-1", uuid), TestHelpers.CreateCallContext());

        response.AcceptedCount.Should().Be(1);
        harness.Registry.GetUnion().Should().BeEquivalentTo([uuid]);
    }

    [Fact]
    public async Task SetPresenceInterest_EmptySet_IsAccepted()
    {
        // Сигнал «за нами больше никто не следит» — по нему менеджер закроет S2S-подписку.
        var harness = CreateHarness();

        var response = await harness.Service.SetPresenceInterest(
            Request("instance-1"), TestHelpers.CreateCallContext());

        response.AcceptedCount.Should().Be(0);
        harness.Registry.LiveInstanceCount.Should().Be(1);
    }

    [Fact]
    public async Task SetPresenceInterest_MissingInstanceId_ThrowsInvalidArgument()
    {
        var harness = CreateHarness();

        var act = async () => await harness.Service.SetPresenceInterest(
            Request("  ", Guid.NewGuid()), TestHelpers.CreateCallContext());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task SetPresenceInterest_OverLimit_IsTruncatedNotRejected()
    {
        // На этой стороне лимит — защита от разрастания, а не отказ: лишние uuid
        // просто не попадут в подписку, а инстанс Onliner продолжит работать.
        var harness = CreateHarness(maxSubscriptionSize: 2);
        var uuids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        var response = await harness.Service.SetPresenceInterest(
            Request("instance-1", uuids), TestHelpers.CreateCallContext());

        response.AcceptedCount.Should().Be(2);
        harness.Registry.GetUnion().Should().HaveCount(2);
    }

    [Fact]
    public async Task SetPresenceInterest_MalformedUuids_AreDropped()
    {
        var harness = CreateHarness();
        var valid = Guid.NewGuid();

        var request = new SetPresenceInterestRequest { InstanceId = "instance-1" };
        request.UserUuids.Add(valid.ToString());
        request.UserUuids.Add("not-a-uuid");

        var response = await harness.Service.SetPresenceInterest(
            request, TestHelpers.CreateCallContext());

        response.AcceptedCount.Should().Be(1);
        harness.Registry.GetUnion().Should().BeEquivalentTo([valid]);
    }
}
