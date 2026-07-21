using Grpc.Core;

namespace BarkFluff.Messages.Tests.Host;

// ServerCallContext — абстрактный с невиртуальными публичными свойствами (Moq их не настроит);
// расширяемость — через protected virtual/abstract *Core-члены. Заглушка по контракту, аналогична
// Tests/BarkFluff.Federation.Tests/Infrastructure/TestServerCallContext.cs.
public sealed class TestServerCallContext : ServerCallContext
{
    private readonly Dictionary<object, object> _userState = new();
    private Status _status = Status.DefaultSuccess;
    private WriteOptions? _writeOptions;

    protected override string MethodCore => "TestMethod";

    protected override string HostCore => "localhost";

    protected override string PeerCore => "ipv4:127.0.0.1:1";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore { get; } = new();

    protected override CancellationToken CancellationTokenCore => CancellationToken.None;

    protected override Metadata ResponseTrailersCore { get; } = new();

    protected override Status StatusCore
    {
        get => _status;
        set => _status = value;
    }

    protected override WriteOptions? WriteOptionsCore
    {
        get => _writeOptions;
        set => _writeOptions = value;
    }

    protected override AuthContext AuthContextCore => null!;

    protected override IDictionary<object, object> UserStateCore => _userState;

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        => Task.CompletedTask;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        => null!;
}
