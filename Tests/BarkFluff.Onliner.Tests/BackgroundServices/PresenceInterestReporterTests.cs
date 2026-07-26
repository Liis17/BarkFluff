using System.Reflection;

using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Proto.FederationInternal;

using Grpc.Core;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class PresenceInterestReporterTests
{
    private static readonly MethodInfo ReportMethod =
        typeof(PresenceInterestReporter).GetMethod("ReportAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly TestHelper _h = new();
    private readonly Mock<FederationInternalApi.FederationInternalApiClient> _federation = new();
    private readonly List<SetPresenceInterestRequest> _sent = [];

    private PresenceInterestReporter CreateReporter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Onliner:PresenceInterestIntervalSeconds"] = "20",
            })
            .Build();

        return new PresenceInterestReporter(
            _h.SubscriptionsManager,
            _federation.Object,
            configuration,
            _h.Metrics,
            TestHelper.CreateLogger<PresenceInterestReporter>());
    }

    private void SetupSuccess()
    {
        _federation
            .Setup(c => c.SetPresenceInterestAsync(
                It.IsAny<SetPresenceInterestRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<SetPresenceInterestRequest, Metadata, DateTime?, CancellationToken>(
                (r, _, _, _) => _sent.Add(r))
            .Returns(new AsyncUnaryCall<SetPresenceInterestResponse>(
                Task.FromResult(new SetPresenceInterestResponse()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private void SetupFailure(StatusCode statusCode)
    {
        _federation
            .Setup(c => c.SetPresenceInterestAsync(
                It.IsAny<SetPresenceInterestRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(statusCode, "boom")));
    }

    private static Task ReportAsync(PresenceInterestReporter reporter)
        => (Task)ReportMethod.Invoke(reporter, [CancellationToken.None])!;

    [Fact]
    public async Task Report_SendsFullTrackedSetNotDelta()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var (stream, _) = TestHelper.CreateCollectingStatusStream();
        _h.SubscriptionsManager.RegisterSubscription(1, [], stream.Object, [first, second]);
        SetupSuccess();

        await ReportAsync(CreateReporter());

        var request = _sent.Should().ContainSingle().Subject;
        request.UserUuids.Should().BeEquivalentTo([first.ToString(), second.ToString()]);
        request.InstanceId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Report_EmptySet_IsStillSent()
    {
        // Пустой набор — сигнал «за нами больше никто не следит»: по нему Federation
        // закрывает S2S-подписку. Молчание вместо него оставило бы её висеть.
        SetupSuccess();

        await ReportAsync(CreateReporter());

        _sent.Should().ContainSingle().Which.UserUuids.Should().BeEmpty();
    }

    [Fact]
    public async Task Report_FederationUnimplemented_IsNotAnError()
    {
        // До этапа 4.3 Federation отвечает Unimplemented — это нормальное состояние.
        SetupFailure(StatusCode.Unimplemented);

        var act = async () => await ReportAsync(CreateReporter());

        await act.Should().NotThrowAsync();
        _h.Metrics.SnapshotAndReset().Should().NotContainKey("presence_interest_errors");
    }

    [Fact]
    public async Task Report_FederationUnavailable_IsSwallowedAndCounted()
    {
        // Ретраев нет by design: следующий тик через N секунд.
        SetupFailure(StatusCode.Unavailable);

        var act = async () => await ReportAsync(CreateReporter());

        await act.Should().NotThrowAsync();
        _h.Metrics.SnapshotAndReset().Should().ContainKey("presence_interest_errors");
    }
}
