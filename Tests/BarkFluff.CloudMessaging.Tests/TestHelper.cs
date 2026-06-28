using Grpc.Core;

namespace BarkFluff.CloudMessaging.Tests;

public static class TestHelper
{
    public static ILogger<T> CreateLogger<T>()
    {
        return Mock.Of<ILogger<T>>();
    }

    public static AsyncUnaryCall<T> CreateAsyncCall<T>(T response) where T : class
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });
    }

    public static Mock<ILogger<T>> CreateLoggerMock<T>()
    {
        return new Mock<ILogger<T>>();
    }
}
