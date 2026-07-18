using Grpc.Core;

using Microsoft.Extensions.Logging;

using Moq;

namespace BarkFluff.GrpcServer.Tests;

public class ServerExceptionInterceptorTests
{
    [Fact]
    public async Task UnaryServerHandler_FailedPrecondition_PreservesStatusWithoutErrorLog()
    {
        var logger = new RecordingLogger<ServerExceptionInterceptor>();
        var interceptor = new ServerExceptionInterceptor(logger);
        var expected = new RpcException(new Status(StatusCode.FailedPrecondition, "No active subscriptions found"));

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<object, object>(
            new object(),
            Mock.Of<ServerCallContext>(),
            (_, _) => throw expected));

        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        logger.Levels.Should().Contain(LogLevel.Warning);
        logger.Levels.Should().NotContain(LogLevel.Error);
    }

    [Fact]
    public async Task UnaryServerHandler_Unavailable_PreservesStatusAndLogsError()
    {
        var logger = new RecordingLogger<ServerExceptionInterceptor>();
        var interceptor = new ServerExceptionInterceptor(logger);
        var expected = new RpcException(new Status(StatusCode.Unavailable, "Error connecting to subchannel"));

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<object, object>(
            new object(),
            Mock.Of<ServerCallContext>(),
            (_, _) => throw expected));

        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        logger.Levels.Should().Contain(LogLevel.Error);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
        }
    }
}
