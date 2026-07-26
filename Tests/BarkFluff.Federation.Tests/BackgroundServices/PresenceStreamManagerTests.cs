using System.Reflection;

using BarkFluff.Federation.BackgroundServices;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Onliner;

using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.BackgroundServices;

/// <summary>
/// Поведение исходящего presence-стрима при обрыве (этап 4.3). Сетевую часть (реальный S2S,
/// реконнект против живой ноды) проверяет E2E на двух-нодовом стенде — здесь проверяется
/// то, что от сети не зависит: статусы гаснут и цикл не сдаётся после первой ошибки.
/// </summary>
public class PresenceStreamManagerTests
{
    private static readonly MethodInfo RunStreamMethod =
        typeof(PresenceStreamManager).GetMethod("RunStreamAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private sealed record Harness(
        PresenceStreamManager Manager,
        Mock<OnlinerServerApi.OnlinerServerApiClient> Onliner,
        List<UpsertRemoteStatusRequest> Upserts);

    private static Harness CreateHarness()
    {
        var configuration = TestHelpers.CreateConfiguration();
        var db = TestHelpers.CreateDatabase();
        var provider = TestHelpers.CreateProvider(db, configuration);
        var options = new PresenceOptions(configuration);
        var metrics = new MetricsCollector();

        var onliner = new Mock<OnlinerServerApi.OnlinerServerApiClient>();
        var upserts = new List<UpsertRemoteStatusRequest>();

        onliner
            .Setup(c => c.UpsertRemoteStatusAsync(
                It.IsAny<UpsertRemoteStatusRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<UpsertRemoteStatusRequest, Metadata, DateTime?, CancellationToken>(
                (r, _, _, _) => upserts.Add(r))
            .Returns(TestHelpers.UnaryCall(new UpsertRemoteStatusResponse()));

        var manager = new PresenceStreamManager(
            new PresenceInterestRegistry(options),
            new RemoteUserServerCache(
                Mock.Of<BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient>(),
                metrics,
                NullLogger<RemoteUserServerCache>.Instance),
            new PeerCapabilityCache(
                provider.GetRequiredService<S2SChannelFactory>(),
                configuration,
                metrics,
                NullLogger<PeerCapabilityCache>.Instance),
            provider.GetRequiredService<S2SChannelFactory>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FederationSwitch(configuration),
            options,
            onliner.Object,
            metrics,
            NullLogger<PresenceStreamManager>.Instance);

        return new Harness(manager, onliner, upserts);
    }

    private static Task RunStreamAsync(
        PresenceStreamManager manager,
        string serverName,
        HashSet<Guid> uuids,
        CancellationToken ct)
        => (Task)RunStreamMethod.Invoke(manager, [serverName, uuids, ct])!;

    [Fact]
    public async Task StreamFailure_ExtinguishesEveryUuidWithUnknown()
    {
        // Нода-партнёр неизвестна → подключение падает. Статусы обязаны погаснуть,
        // а не «залипнуть онлайн»: источник истины недоступен, и врать клиенту нельзя.
        var harness = CreateHarness();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        using var cts = new CancellationTokenSource(600);
        await RunStreamAsync(harness.Manager, "unknown-node.test", [first, second], cts.Token);

        harness.Upserts.Should().NotBeEmpty();
        harness.Upserts.Should().OnlyContain(u => u.Status == StatusTypeId.Unknown);
        harness.Upserts.Select(u => u.UserUuid).Should()
            .Contain([first.ToString(), second.ToString()]);
    }

    [Fact]
    public async Task StreamFailure_RetriesWithBackoffInsteadOfGivingUp()
    {
        // Ретраев по СОБЫТИЯМ нет by design, но сам стрим обязан переподключаться —
        // иначе после первого сетевого сбоя presence умер бы до перезапуска сервиса.
        var harness = CreateHarness();
        var uuid = Guid.NewGuid();

        using var cts = new CancellationTokenSource(2500);
        await RunStreamAsync(harness.Manager, "unknown-node.test", [uuid], cts.Token);

        // Первая попытка + минимум одна после backoff (1с) → как минимум два гашения.
        harness.Upserts.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task CancelledStream_StopsPromptly()
    {
        var harness = CreateHarness();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await RunStreamAsync(
            harness.Manager, "unknown-node.test", [Guid.NewGuid()], cts.Token);

        await act.Should().NotThrowAsync();
    }
}
