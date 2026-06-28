using BarkFluff.Messages.Infrastructure.Behaviors;

namespace BarkFluff.Messages.Tests.Infrastructure;

public class MetricsBehaviorTests
{
    private readonly MetricsCollector _metrics = new();

    [Fact]
    public async Task Handle_Success_IncrementsSuccessAndRequests()
    {
        var behavior = new MetricsBehavior<TestCommand, string>(_metrics);
        var result = await behavior.Handle(new TestCommand(), (ct) => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_Exception_IncrementsErrorsAndRethrows()
    {
        var behavior = new MetricsBehavior<TestCommand, string>(_metrics);

        var act = async () => await behavior.Handle(new TestCommand(), (ct) => throw new InvalidOperationException("test"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private class TestCommand { }
}
