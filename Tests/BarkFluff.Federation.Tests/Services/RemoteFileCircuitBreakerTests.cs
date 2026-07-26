using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;

namespace BarkFluff.Federation.Tests.Services;

/// <summary>
/// Circuit breaker скачивания файлов per-origin (этап 3.5).
/// </summary>
public class RemoteFileCircuitBreakerTests
{
    private const string Server = "node2.test";

    private static RemoteFileCircuitBreaker Create(
        TestTimeProvider time,
        int failures = 3,
        int openSeconds = 60)
        => new(
            TestHelpers.CreateConfiguration(new Dictionary<string, string?>
            {
                ["Federation:RemoteFileCircuitFailures"] = failures.ToString(),
                ["Federation:RemoteFileCircuitOpenSeconds"] = openSeconds.ToString(),
            }),
            new MetricsCollector(),
            time);

    [Fact]
    public void FreshServer_IsAllowed()
    {
        var breaker = Create(new TestTimeProvider());

        breaker.TryEnter(Server).Should().BeTrue();
        breaker.IsOpen(Server).Should().BeFalse();
    }

    [Fact]
    public void FailuresBelowThreshold_KeepCircuitClosed()
    {
        var breaker = Create(new TestTimeProvider(), failures: 3);

        breaker.RecordFailure(Server);
        breaker.RecordFailure(Server);

        breaker.TryEnter(Server).Should().BeTrue();
    }

    [Fact]
    public void ThresholdReached_OpensCircuit()
    {
        // Лежащая нода не должна съедать connect-timeout на каждом обращении.
        var breaker = Create(new TestTimeProvider(), failures: 3);

        for (var i = 0; i < 3; i++)
        {
            breaker.RecordFailure(Server);
        }

        breaker.IsOpen(Server).Should().BeTrue();
        breaker.TryEnter(Server).Should().BeFalse();
    }

    [Fact]
    public void SuccessResetsFailureCount()
    {
        var breaker = Create(new TestTimeProvider(), failures: 3);

        breaker.RecordFailure(Server);
        breaker.RecordFailure(Server);
        breaker.RecordSuccess(Server);
        breaker.RecordFailure(Server);
        breaker.RecordFailure(Server);

        breaker.TryEnter(Server).Should().BeTrue();
    }

    [Fact]
    public void AfterWindow_ProbeIsAllowed()
    {
        var time = new TestTimeProvider();
        var breaker = Create(time, failures: 2, openSeconds: 60);

        breaker.RecordFailure(Server);
        breaker.RecordFailure(Server);
        breaker.TryEnter(Server).Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(61));

        // Half-open: запрос пропускается, он и есть пробный.
        breaker.TryEnter(Server).Should().BeTrue();
    }

    [Fact]
    public void ProbeSuccess_ClosesCircuit()
    {
        var time = new TestTimeProvider();
        var breaker = Create(time, failures: 2, openSeconds: 60);

        breaker.RecordFailure(Server);
        breaker.RecordFailure(Server);
        time.Advance(TimeSpan.FromSeconds(61));

        breaker.RecordSuccess(Server);

        breaker.IsOpen(Server).Should().BeFalse();
        breaker.TryEnter(Server).Should().BeTrue();
    }

    [Fact]
    public void ProbeFailure_ReopensCircuitForNewWindow()
    {
        var time = new TestTimeProvider();
        var breaker = Create(time, failures: 2, openSeconds: 60);

        breaker.RecordFailure(Server);
        breaker.RecordFailure(Server);
        time.Advance(TimeSpan.FromSeconds(61));

        // Пробный запрос снова не удался — окно открывается заново.
        breaker.RecordFailure(Server);

        breaker.TryEnter(Server).Should().BeFalse();
        time.Advance(TimeSpan.FromSeconds(61));
        breaker.TryEnter(Server).Should().BeTrue();
    }

    [Fact]
    public void CircuitIsPerServer()
    {
        // Мёртвая нода не должна блокировать скачивание с живых.
        var breaker = Create(new TestTimeProvider(), failures: 2);

        breaker.RecordFailure(Server);
        breaker.RecordFailure(Server);

        breaker.TryEnter(Server).Should().BeFalse();
        breaker.TryEnter("node3.test").Should().BeTrue();
    }

    [Fact]
    public void ServerNameIsCaseInsensitive()
    {
        var breaker = Create(new TestTimeProvider(), failures: 2);

        breaker.RecordFailure("Node2.TEST");
        breaker.RecordFailure(Server);

        breaker.TryEnter(Server).Should().BeFalse();
    }
}
