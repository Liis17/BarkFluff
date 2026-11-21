using BarkFluff.Status.Probing;
using Xunit;

namespace BarkFluff.Status.Tests;

public class ReadinessClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static string Body(string status, DateTimeOffset checkedAt, string extra = "") =>
        $$"""{"status":"{{status}}","checkedAtUtc":"{{checkedAt.UtcDateTime:O}}","checks":[{"name":"db","status":"healthy"}],"instanceId":"abc"{{extra}}}""";

    [Theory]
    [InlineData("healthy", 200, ProbeState.Up)]
    [InlineData("degraded", 200, ProbeState.Degraded)]
    [InlineData("unknown", 200, ProbeState.Unknown)]
    [InlineData("down", 503, ProbeState.Down)]
    public void FreshSnapshotMapsToContractState(string status, int code, ProbeState expected)
    {
        Assert.Equal(expected, ReadinessClassifier.Classify(code, Body(status, Now), Now));
    }

    [Theory]
    [InlineData("healthy", 503)]
    [InlineData("down", 200)]
    [InlineData("degraded", 503)]
    [InlineData("unknown", 503)]
    [InlineData("healthy", 301)]
    [InlineData("alive", 200)]
    public void UnexpectedStatusCodeOrValueIsDown(string status, int code)
    {
        Assert.Equal(ProbeState.Down, ReadinessClassifier.Classify(code, Body(status, Now), Now));
    }

    [Fact]
    public void StartingWithinGraceIsStartingAndExcluded()
    {
        var checkedAt = Now - ReadinessClassifier.StartingGrace + TimeSpan.FromSeconds(1);
        Assert.Equal(ProbeState.Starting, ReadinessClassifier.Classify(200, Body("starting", checkedAt), Now));
    }

    [Fact]
    public void ProlongedStartingIsDownOnceGraceIsOver()
    {
        var checkedAt = Now - ReadinessClassifier.StartingGrace - TimeSpan.FromSeconds(1);
        Assert.Equal(ProbeState.Down, ReadinessClassifier.Classify(200, Body("starting", checkedAt), Now));
    }

    [Fact]
    public void StartingWithServerErrorIsDown()
    {
        Assert.Equal(ProbeState.Down, ReadinessClassifier.Classify(503, Body("starting", Now), Now));
    }

    [Fact]
    public void OldValidSnapshotIsStaleNotDown()
    {
        var checkedAt = Now - ReadinessClassifier.MaxAge - TimeSpan.FromSeconds(1);
        Assert.Equal(ProbeState.Stale, ReadinessClassifier.Classify(200, Body("healthy", checkedAt), Now));
    }

    [Theory]
    [InlineData("healthy", 200, false)]
    [InlineData("degraded", 200, false)]
    [InlineData("unknown", 200, false)]
    [InlineData("healthy", 200, true)]
    [InlineData("degraded", 200, true)]
    [InlineData("unknown", 200, true)]
    public void StaleOrFutureValidSnapshotIsStale(string status, int code, bool future)
    {
        var offset = future
            ? ReadinessClassifier.MaxFutureSkew + TimeSpan.FromSeconds(1)
            : -(ReadinessClassifier.MaxAge + TimeSpan.FromSeconds(1));
        Assert.Equal(ProbeState.Stale, ReadinessClassifier.Classify(code, Body(status, Now + offset), Now));
    }

    [Theory]
    [InlineData("down", 503, false)]
    [InlineData("down", 503, true)]
    [InlineData("healthy", 503, false)]
    [InlineData("healthy", 503, true)]
    [InlineData("down", 200, false)]
    [InlineData("alive", 200, false)]
    [InlineData("alive", 200, true)]
    [InlineData("unknown", 503, false)]
    [InlineData("healthy", 301, false)]
    public void InvalidContractOrDownIsDownRegardlessOfAge(string status, int code, bool future)
    {
        var offset = future
            ? ReadinessClassifier.MaxFutureSkew + TimeSpan.FromSeconds(1)
            : -(ReadinessClassifier.MaxAge + TimeSpan.FromSeconds(1));
        Assert.Equal(ProbeState.Down, ReadinessClassifier.Classify(code, Body(status, Now + offset), Now));
    }

    [Fact]
    public void SnapshotFromFutureBeyondSkewIsStale()
    {
        var checkedAt = Now + ReadinessClassifier.MaxFutureSkew + TimeSpan.FromSeconds(1);
        Assert.Equal(ProbeState.Stale, ReadinessClassifier.Classify(200, Body("healthy", checkedAt), Now));
    }

    [Fact]
    public void ExtraFieldsAreIgnored()
    {
        var extra = ",\"dependencies\":{\"secret\":\"do-not-read\"},\"version\":\"9.9\"";
        var body = Body("healthy", Now, extra);
        Assert.Equal(ProbeState.Up, ReadinessClassifier.Classify(200, body, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>gateway</html>")]
    [InlineData("[]")]
    [InlineData("{\"status\":\"healthy\"}")]
    [InlineData("{\"status\":\"healthy\",\"checkedAtUtc\":\"not-a-time\"}")]
    [InlineData("{\"status\":7,\"checkedAtUtc\":\"2026-11-21T12:00:00Z\"}")]
    public void UnparseableOrMissingBodyIsDown(string? body)
    {
        Assert.Equal(ProbeState.Down, ReadinessClassifier.Classify(200, body, Now));
    }

    [Fact]
    public void BodyOverLimitIsDown()
    {
        // Ограничение чтения применяется в ReadinessProbe: превышение даёт null, а null — Down.
        Assert.Equal(ProbeState.Down, ReadinessClassifier.Classify(200, null, Now));
    }
}
