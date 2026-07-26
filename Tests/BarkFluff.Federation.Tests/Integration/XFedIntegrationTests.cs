using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Exceptions.Federation;

using FluentAssertions;

using Grpc.Core;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace BarkFluff.Federation.Tests.Integration;

// Критерии готовности этапа 1.3 (docs/rearch/phase-1/step-1.3-xfed-signing.md, Изменение 7.1):
// валидный Ping — OK; битая подпись — Unauthenticated; чужой destination — отказ;
// timestamp за окном — отказ с ClockSkewDetected; заблокированный origin — PermissionDenied.
public class XFedIntegrationTests
{
    private const string PingMethod = "/barkfluff.federation.FederationS2SApi/Ping";
    private const string GetServerKeysMethod = "/barkfluff.federation.FederationS2SApi/GetServerKeys";
    private const string OwnServerName = "node-a.test";
    private const string PeerServerName = "node-b.test";

    private static (byte[] Seed, byte[] Pub) GenerateKeyPair()
    {
        var random = new SecureRandom();
        var kpGen = new Ed25519KeyPairGenerator();
        kpGen.Init(new KeyGenerationParameters(random, 256));
        var kp = kpGen.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)kp.Private;
        var pub = (Ed25519PublicKeyParameters)kp.Public;
        return (priv.GetEncoded(), pub.GetEncoded());
    }

    [Fact]
    public async Task Ping_ValidSignature_ReturnsOk()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName);
        var (seed, pub) = GenerateKeyPair();
        await host.SeedPeerAsync(PeerServerName, pub);

        var client = host.CreateClient();
        var request = new PingRequest { OriginServer = PeerServerName };
        var headers = SignedRequestBuilder.BuildHeaders(PeerServerName, OwnServerName, "ed25519:1", seed, PingMethod, request);

        var response = await client.PingAsync(request, headers);

        response.ServerName.Should().Be(OwnServerName);
    }

    [Fact]
    public async Task Ping_MissingHeaders_ThrowsUnauthenticated()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName);
        var client = host.CreateClient();
        var request = new PingRequest { OriginServer = PeerServerName };

        var act = async () => await client.PingAsync(request);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task Ping_TamperedSignature_ThrowsUnauthenticated()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName);
        var (seed, pub) = GenerateKeyPair();
        await host.SeedPeerAsync(PeerServerName, pub);

        var client = host.CreateClient();
        var request = new PingRequest { OriginServer = PeerServerName };
        var headers = SignedRequestBuilder.BuildHeaders(PeerServerName, OwnServerName, "ed25519:1", seed, PingMethod, request);

        var signatureEntry = headers.First(m => m.Key == XFedHeaders.Signature);
        var badSignature = Convert.FromBase64String(signatureEntry.Value);
        badSignature[0] ^= 0xFF;
        headers.Remove(signatureEntry);
        headers.Add(XFedHeaders.Signature, Convert.ToBase64String(badSignature));

        var act = async () => await client.PingAsync(request, headers);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task Ping_WrongDestination_ThrowsUnauthenticated()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName);
        var (seed, pub) = GenerateKeyPair();
        await host.SeedPeerAsync(PeerServerName, pub);

        var client = host.CreateClient();
        var request = new PingRequest { OriginServer = PeerServerName };
        var headers = SignedRequestBuilder.BuildHeaders(PeerServerName, "someone-else.test", "ed25519:1", seed, PingMethod, request);

        var act = async () => await client.PingAsync(request, headers);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task Ping_TimestampOutsideWindow_ThrowsUnauthenticatedWithClockSkewCode()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName, signatureWindowSeconds: 60);
        var (seed, pub) = GenerateKeyPair();
        await host.SeedPeerAsync(PeerServerName, pub);

        var client = host.CreateClient();
        var request = new PingRequest { OriginServer = PeerServerName };
        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();
        var headers = SignedRequestBuilder.BuildHeaders(PeerServerName, OwnServerName, "ed25519:1", seed, PingMethod, request, staleTimestamp);

        var act = async () => await client.PingAsync(request, headers);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        ex.Which.Trailers.GetValue("x-error-code").Should().Be(new ClockSkewDetectedException().ErrorCode);
    }

    [Fact]
    public async Task Ping_BlockedOrigin_ThrowsPermissionDenied()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName);
        var (seed, pub) = GenerateKeyPair();
        await host.SeedPeerAsync(PeerServerName, pub, status: KnownServerStatus.Blocked);

        var client = host.CreateClient();
        var request = new PingRequest { OriginServer = PeerServerName };
        var headers = SignedRequestBuilder.BuildHeaders(PeerServerName, OwnServerName, "ed25519:1", seed, PingMethod, request);

        var act = async () => await client.PingAsync(request, headers);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task GetServerKeys_ExemptFromSignatureCheck_Succeeds()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName);
        var client = host.CreateClient();

        var response = await client.GetServerKeysAsync(new GetServerKeysRequest());

        response.ServerName.Should().Be(OwnServerName);
    }

    // P1-04: при Federation:Enabled=false нода не принимает S2S-трафик — включая bootstrap GetServerKeys.
    [Fact]
    public async Task Ping_FederationDisabled_ThrowsFailedPrecondition()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName, enabled: false);
        var client = host.CreateClient();

        var act = async () => await client.PingAsync(new PingRequest { OriginServer = PeerServerName });

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        ex.Which.Trailers.GetValue("x-error-code").Should().Be(new FederationNotConfiguredException().ErrorCode);
    }

    [Fact]
    public async Task GetServerKeys_FederationDisabled_ThrowsFailedPrecondition()
    {
        await using var host = await FederationTestHost.CreateAsync(OwnServerName, enabled: false);
        var client = host.CreateClient();

        var act = async () => await client.GetServerKeysAsync(new GetServerKeysRequest());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    // P1-05: при пустом Federation:ServerName GetServerKeys не отдаёт ключи (нода не сконфигурирована).
    [Fact]
    public async Task GetServerKeys_EmptyServerName_ThrowsFailedPrecondition()
    {
        await using var host = await FederationTestHost.CreateAsync(ownServerName: "");
        var client = host.CreateClient();

        var act = async () => await client.GetServerKeysAsync(new GetServerKeysRequest());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }
}
