using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Services;

using FluentAssertions;

namespace BarkFluff.Federation.Tests.Services;

// P1-09: reconciliation ключей пира — add/sync/revoke, с защитой от переписывания pubkey под тем же key_id.
public class KnownServerKeyReconcilerTests
{
    private static byte[] Key(byte b) => Enumerable.Repeat(b, 32).ToArray();

    private static KnownServer Server(params KnownServerKey[] keys)
        => new() { ServerName = "peer.test", Keys = keys.ToList() };

    [Fact]
    public void Reconcile_NewKeyId_Added()
    {
        var server = Server(new KnownServerKey { ServerName = "peer.test", KeyId = "ed25519:1", PublicKey = Key(1) });

        KnownServerKeyReconciler.Reconcile(server,
            [new RemoteSigningKey("ed25519:1", Key(1), null), new RemoteSigningKey("ed25519:2", Key(2), null)], DateTime.UtcNow);

        server.Keys.Should().Contain(k => k.KeyId == "ed25519:2");
        server.Keys.Single(k => k.KeyId == "ed25519:1").RevokedAt.Should().BeNull();
    }

    [Fact]
    public void Reconcile_ExistingSamePubkey_SyncsExpiry()
    {
        var server = Server(new KnownServerKey { ServerName = "peer.test", KeyId = "ed25519:1", PublicKey = Key(1), ExpiredAt = null });
        var expiry = DateTime.UtcNow.AddDays(30);

        KnownServerKeyReconciler.Reconcile(server, [new RemoteSigningKey("ed25519:1", Key(1), expiry)], DateTime.UtcNow);

        server.Keys.Single().ExpiredAt.Should().Be(expiry);
    }

    [Fact]
    public void Reconcile_ExistingDifferentPubkey_NotOverwritten()
    {
        var server = Server(new KnownServerKey { ServerName = "peer.test", KeyId = "ed25519:1", PublicKey = Key(1) });

        KnownServerKeyReconciler.Reconcile(server, [new RemoteSigningKey("ed25519:1", Key(9), null)], DateTime.UtcNow);

        server.Keys.Single(k => k.KeyId == "ed25519:1").PublicKey.Should().Equal(Key(1)); // не переписан
    }

    [Fact]
    public void Reconcile_AbsentKey_Revoked()
    {
        var server = Server(
            new KnownServerKey { ServerName = "peer.test", KeyId = "ed25519:1", PublicKey = Key(1) },
            new KnownServerKey { ServerName = "peer.test", KeyId = "ed25519:0", PublicKey = Key(0) });
        var now = DateTime.UtcNow;

        KnownServerKeyReconciler.Reconcile(server, [new RemoteSigningKey("ed25519:1", Key(1), null)], now);

        server.Keys.Single(k => k.KeyId == "ed25519:0").RevokedAt.Should().Be(now);
        server.Keys.Single(k => k.KeyId == "ed25519:1").RevokedAt.Should().BeNull();
    }
}
