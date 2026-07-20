using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;

using Google.Protobuf;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.Federation.Tests.Services;

public class XFedClientInterceptorTests
{
    private const string Destination = "peer.test";

    private static readonly Marshaller<PingRequest> PingRequestMarshaller = Marshallers.Create(
        m => ((IMessage)m).ToByteArray(),
        b => PingRequest.Parser.ParseFrom(b));

    private static readonly Marshaller<PingResponse> PingResponseMarshaller = Marshallers.Create(
        m => ((IMessage)m).ToByteArray(),
        b => PingResponse.Parser.ParseFrom(b));

    private static readonly Method<PingRequest, PingResponse> PingMethod = new(
        MethodType.Unary,
        "barkfluff.federation.FederationS2SApi",
        "Ping",
        PingRequestMarshaller,
        PingResponseMarshaller);

    private static ClientInterceptorContext<PingRequest, PingResponse> CreateContext(Metadata? headers = null)
        => new(PingMethod, "peer-host", new CallOptions(headers ?? new Metadata()));

    private static AsyncUnaryCall<PingResponse> FakeCall()
        => TestHelpers.UnaryCall(new PingResponse());

    private static async Task<(XFedClientInterceptor Interceptor, FederationSigningKey Key)> CreateInterceptorAsync()
    {
        var db = TestHelpers.CreateDatabase();
        FederationSigningKey key;
        await using (var seedContext = TestHelpers.CreateContext(db))
        {
            key = await TestHelpers.EnsureActiveKeyAsync(seedContext);
        }

        var provider = TestHelpers.CreateProvider(db);
        var keyCache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());
        await keyCache.RefreshAsync();

        var interceptor = new XFedClientInterceptor(
            TestHelpers.CreateConfiguration(),
            keyCache,
            new MetricsCollector(),
            Destination);

        return (interceptor, key);
    }

    [Fact]
    public async Task AsyncUnaryCall_AddsSignedXFedHeaders()
    {
        var (interceptor, key) = await CreateInterceptorAsync();
        var request = new PingRequest { OriginServer = TestHelpers.OwnServerName };

        var captured = default(ClientInterceptorContext<PingRequest, PingResponse>);
        var call = interceptor.AsyncUnaryCall(request, CreateContext(), (req, ctx) =>
        {
            captured = ctx;
            return FakeCall();
        });
        await call.ResponseAsync;

        var headers = captured.Options.Headers;
        headers.GetValue(XFedHeaders.Origin).Should().Be(TestHelpers.OwnServerName);
        headers.GetValue(XFedHeaders.Destination).Should().Be(Destination);
        headers.GetValue(XFedHeaders.KeyId).Should().Be(key.KeyId);
        long.Parse(headers.GetValue(XFedHeaders.Timestamp)!).Should().BeCloseTo(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 10_000);

        // Подпись обязана сходиться канонической строкой от wire-байтов запроса.
        var canonical = XFedCanonicalString.Build(
            TestHelpers.OwnServerName,
            Destination,
            long.Parse(headers.GetValue(XFedHeaders.Timestamp)!),
            PingMethod.FullName,
            request.ToByteArray());
        var signature = Convert.FromBase64String(headers.GetValue(XFedHeaders.Signature)!);
        SigningKeyService.Verify(key.PublicKey, canonical, signature).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncUnaryCall_PreservesExistingHeaders()
    {
        var (interceptor, _) = await CreateInterceptorAsync();
        var existing = new Metadata { { "custom-header", "custom-value" } };

        var captured = default(ClientInterceptorContext<PingRequest, PingResponse>);
        var call = interceptor.AsyncUnaryCall(new PingRequest(), CreateContext(existing), (req, ctx) =>
        {
            captured = ctx;
            return FakeCall();
        });
        await call.ResponseAsync;

        captured.Options.Headers.GetValue("custom-header").Should().Be("custom-value");
        captured.Options.Headers.GetValue(XFedHeaders.Signature).Should().NotBeNull();
    }

    [Fact]
    public async Task BlockingUnaryCall_AddsSignedXFedHeaders()
    {
        var (interceptor, key) = await CreateInterceptorAsync();
        var request = new PingRequest();

        var captured = default(ClientInterceptorContext<PingRequest, PingResponse>);
        interceptor.BlockingUnaryCall(request, CreateContext(), (req, ctx) =>
        {
            captured = ctx;
            return new PingResponse();
        });

        captured.Options.Headers.GetValue(XFedHeaders.KeyId).Should().Be(key.KeyId);
        captured.Options.Headers.GetValue(XFedHeaders.Signature).Should().NotBeNull();
    }

    [Fact]
    public void AsyncUnaryCall_NoActiveKey_Throws()
    {
        var provider = TestHelpers.CreateProvider(TestHelpers.CreateDatabase());
        var emptyCache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());
        var interceptor = new XFedClientInterceptor(
            TestHelpers.CreateConfiguration(),
            emptyCache,
            new MetricsCollector(),
            Destination);

        var act = () => interceptor.AsyncUnaryCall(new PingRequest(), CreateContext(), (req, ctx) => FakeCall());

        act.Should().Throw<InvalidOperationException>();
    }
}
