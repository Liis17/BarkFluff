using System.Text;
using BarkFluff.Client.Core.Services.SecretChats;
using BarkFluff.Proto.Shared;
using Google.Protobuf;
using Microsoft.Extensions.Time.Testing;
using static BarkFluff.Client.Core.Infrastructure.Storage.SqliteApplicationDataStore;

namespace BarkFluff.Client.WinUI.Tests;

[Collection("Signal shim")]
public sealed class SecretChatServiceTests
{
    private const string Node = "https://test.invalid";

    [Fact]
    public async Task InviteExchangeAndReorderedMessagesSurviveRestartWithoutDuplicateHistory()
    {
        using var pair = await Pair.CreateAsync();
        var (alice, bob) = await pair.ConnectAsync();
        await pair.Alice.SendMessageAsync(alice.Id, "first");
        await pair.Alice.SendMessageAsync(alice.Id, "second");
        var first = pair.BobEndpoint.Messages[0];
        var second = pair.BobEndpoint.Messages[1];

        var reopenedBob = pair.OpenBob();
        Assert.Equal("second", (await reopenedBob.ReceiveMessageAsync(second)).Message!.Text);
        Assert.Equal("first", (await reopenedBob.ReceiveMessageAsync(first)).Message!.Text);
        Assert.True((await reopenedBob.ReceiveMessageAsync(second)).Duplicate);
        var alias = second.Clone();
        alias.MessageId = "alias";
        Assert.True((await reopenedBob.ReceiveMessageAsync(alias)).Duplicate);
        await reopenedBob.SendMessageAsync(bob.Id, "reply");
        Assert.Equal("reply", (await pair.Alice.ReceiveMessageAsync(Assert.Single(pair.AliceEndpoint.Messages))).Message!.Text);
        Assert.Equal(3, (await reopenedBob.ReadHistoryAsync(bob.Id)).Count(message => !message.Outgoing));
        Assert.Equal(new[] { second.MessageId, first.MessageId, second.MessageId, "alias" }, pair.BobEndpoint.Acks);
    }

    [Fact]
    public async Task CrossedInvitesMergeAndRepeatedOrEarlyEnvelopeIsRecordedOnce()
    {
        using var pair = await Pair.CreateAsync();
        pair.BobEndpoint.OnAccept = () => throw new IOException();
        var a = await pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "alice");
        var b = await pair.Bob.CreateChatAsync(1, pair.AliceEndpoint.Id, "bob");
        var fromAlice = Assert.Single(pair.BobEndpoint.Invites);
        var fromBob = Assert.Single(pair.AliceEndpoint.Invites);
        await pair.Bob.ReceiveMessageAsync(new SecretEnvelope
        {
            MessageId = "early", RecipientDeviceId = pair.BobEndpoint.Id, SenderUserId = 1,
            SenderDeviceId = fromAlice.SenderDeviceId, Envelope = fromAlice.InitialEnvelope
        });
        await Assert.ThrowsAsync<IOException>(() => pair.Bob.AcceptInviteAsync(fromAlice));
        pair.BobEndpoint.OnAccept = null;
        var restartedBob = pair.OpenBob();
        await restartedBob.RetryPendingOperationsAsync();
        Assert.Equal(b.Id, (await restartedBob.AcceptInviteAsync(fromAlice)).Id);
        Assert.Equal(a.Id, (await pair.Alice.AcceptInviteAsync(fromBob)).Id);
        await restartedBob.ApplyResolutionAsync(Assert.Single(pair.BobEndpoint.Resolutions));
        await pair.Alice.ApplyResolutionAsync(Assert.Single(pair.AliceEndpoint.Resolutions));
        var history = await restartedBob.ReadHistoryAsync(b.Id);
        Assert.Equal(new[] { "alice", "bob" }, history.Select(message => message.Text).Order());
        await pair.Alice.SendMessageAsync(a.Id, "crossed reply");
        Assert.Equal("crossed reply",
            (await restartedBob.ReceiveMessageAsync(Assert.Single(pair.BobEndpoint.Messages))).Message!.Text);
    }

    [Fact]
    public async Task RejectedInviteRemovesPendingChatAndAllowsANewInvite()
    {
        using var pair = await Pair.CreateAsync();
        await pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "one");
        await pair.Bob.RejectInviteAsync(Assert.Single(pair.BobEndpoint.Invites).InviteId);
        await pair.Alice.ApplyResolutionAsync(Assert.Single(pair.AliceEndpoint.Resolutions));
        Assert.Empty(await pair.Alice.ListChatsAsync());
        Assert.Empty(await pair.Bob.ListChatsAsync());
        var (alice, _) = await pair.ConnectAsync();
        Assert.Equal("initial", Assert.Single(await pair.Alice.ReadHistoryAsync(alice.Id)).Text);
    }

    [Fact]
    public async Task BundleAndFirstEncryptCommitTogetherAndStaleWriterCannotOverwrite()
    {
        using var pair = await Pair.CreateAsync();
        var store = pair.AliceDatabase.Store;
        var scope = pair.AliceScope;
        var before = store.ReadSecretSnapshot(scope);
        await Assert.ThrowsAsync<SignalProtocolException>(() =>
            pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, new string('x', 1024 * 1024)));
        Assert.Equal(before.Revision, store.ReadSecretSnapshot(scope).Revision);
        Assert.Empty(await pair.Alice.ListChatsAsync());
        Assert.Empty(pair.BobEndpoint.Invites);

        pair.AliceEndpoint.OnFetch = () =>
        {
            var other = SignalProtocol.GeneratePreKey(before.State, before.Generation, 100_000);
            pair.AliceDatabase.Store.CommitSecret(scope, before, other.State, other.Generation, _ => { });
        };
        var conflict = await Assert.ThrowsAsync<SignalProtocolException>(() =>
            pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "stale"));
        Assert.Equal(SignalError.GenerationMismatch, conflict.Error);
        Assert.Equal(before.Generation + 1, store.ReadSecretSnapshot(scope).Generation);
        Assert.Empty(await pair.Alice.ListChatsAsync());

        pair.AliceEndpoint.OnFetch = null;
        pair.AliceEndpoint.OnSendInvite = () => throw new IOException();
        await Assert.ThrowsAsync<IOException>(() => pair.OpenAlice().CreateChatAsync(2, pair.BobEndpoint.Id, "pending"));
        Assert.Empty(pair.BobEndpoint.Invites);
        var committed = store.ReadSecretSnapshot(scope);
        pair.AliceEndpoint.OnSendInvite = null;
        await pair.OpenAlice().RetryPendingOperationsAsync();
        Assert.Equal(committed.Generation, store.ReadSecretSnapshot(scope).Generation);
        Assert.Single(pair.BobEndpoint.Invites);
    }

    [Fact]
    public async Task RestartAfterSpeculativeDecryptKeepsRatchetAndAcksOnlyDurableHistory()
    {
        using var pair = await Pair.CreateAsync();
        var (alice, bob) = await pair.ConnectAsync();
        await pair.Alice.SendMessageAsync(alice.Id, "private persisted text");
        var envelope = Assert.Single(pair.BobEndpoint.Messages);
        var store = pair.BobDatabase.Store;
        var scope = pair.BobScope;
        var before = store.ReadSecretSnapshot(scope);
        var result = SignalProtocol.Decrypt(before.State, before.Generation, envelope.SenderDeviceId,
            SignalMessageKind.PreKey, envelope.Envelope.ToByteArray());
        Assert.Throws<IOException>(() => store.CommitSecret(scope, before, result.State, result.Generation,
            writer =>
            {
                writer.Set(SecretTable.History, "interrupted", new SecretChatMessage("interrupted", bob.Id, 1,
                    envelope.SenderDeviceId, DateTimeOffset.UtcNow, "partial"));
                throw new IOException();
            }));
        Assert.Equal(before.Revision, store.ReadSecretSnapshot(scope).Revision);
        Assert.Empty(pair.BobEndpoint.Acks);

        var inspected = 0;
        pair.BobEndpoint.BeforeAck = id =>
        {
            Assert.Equal(envelope.MessageId, id);
            Assert.Contains(store.ReadSecrets<SecretChatMessage>(scope, SecretTable.History),
                message => message.ServerId == id && message.Text == "private persisted text");
            Assert.DoesNotContain(store.ReadSecrets<SecretChatMessage>(scope, SecretTable.History),
                message => message.Id == "interrupted");
            if (++inspected == 1)
                throw new IOException();
        };
        await Assert.ThrowsAsync<IOException>(() => pair.OpenBob().ReceiveMessageAsync(envelope));
        await pair.OpenBob().RetryPendingOperationsAsync();
        Assert.True((await pair.OpenBob().ReceiveMessageAsync(envelope)).Duplicate);
        Assert.Equal(2, (await pair.OpenBob().ReadHistoryAsync(bob.Id)).Count);
        var path = pair.BobDatabase.Paths.DatabasePath;
        foreach (var file in new[] { path, path + "-wal" }.Where(File.Exists))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            Assert.False(Encoding.UTF8.GetString(bytes.ToArray()).Contains("private persisted text", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task PrekeyPublicationRecoversAfterLostRepliesAndRetainsKeysForDelayedInvites()
    {
        using var pair = await Pair.CreateAsync();
        var alice = await pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "delayed");
        var original = pair.BobEndpoint.Registration!.Clone();
        Assert.Equal(100, original.OneTimePrekeys.Count);
        Assert.Equal(100, original.OneTimeKyberPrekeys.Count);
        pair.BobEndpoint.Ec.RemoveRange(0, 89);
        pair.BobEndpoint.Kyber.RemoveRange(0, 90);
        await pair.Bob.SynchronizePrekeysAsync();
        var replenished = Assert.Single(pair.BobEndpoint.Replenishments);
        Assert.Empty(replenished.Prekeys);
        Assert.Equal(91, replenished.KyberPrekeys.Count);
        Assert.True(replenished.KyberPrekeys.Min(key => key.PrekeyId) > original.OneTimeKyberPrekeys.Max(key => key.PrekeyId));

        pair.Clock.Advance(TimeSpan.FromDays(8));
        pair.BobEndpoint.AfterRotate = () => throw new IOException();
        await Assert.ThrowsAsync<IOException>(() => pair.Bob.SynchronizePrekeysAsync());
        pair.BobEndpoint.AfterRotate = null;
        // Нативные метки времени привязаны к часам ОС, а просрочку моделирует FakeTimeProvider.
        pair.Clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var bob = pair.OpenBob();
        await bob.SynchronizePrekeysAsync();
        var rotation = Assert.Single(pair.BobEndpoint.Rotations);
        Assert.True(rotation.SignedPrekey.PrekeyId > original.SignedPrekey.PrekeyId);
        Assert.True(rotation.LastResortKyberPrekey.PrekeyId > replenished.KyberPrekeys.Max(key => key.PrekeyId));

        pair.BobEndpoint.Ec.Clear();
        pair.BobEndpoint.AfterReplenish = () => throw new IOException();
        await Assert.ThrowsAsync<IOException>(() => bob.SynchronizePrekeysAsync());
        pair.BobEndpoint.AfterReplenish = null;
        await pair.OpenBob().SynchronizePrekeysAsync();
        Assert.Equal(pair.BobEndpoint.Replenishments[1], pair.BobEndpoint.Replenishments[2]);
        Assert.True(pair.BobEndpoint.Replenishments[1].Prekeys.Min(key => key.PrekeyId) >
            original.OneTimePrekeys.Max(key => key.PrekeyId));
        var accepted = await bob.AcceptInviteAsync(Assert.Single(pair.BobEndpoint.Invites));
        Assert.Equal("delayed", Assert.Single(await bob.ReadHistoryAsync(accepted.Id)).Text);
        await pair.Alice.ApplyResolutionAsync(Assert.Single(pair.AliceEndpoint.Resolutions));
        Assert.Equal(SecretChatStatus.Accepted, Assert.Single(await pair.Alice.ListChatsAsync()).Status);
        Assert.Equal(alice.Id, Assert.Single(await pair.Alice.ListChatsAsync()).Id);

        pair.BobEndpoint.MapStatus = status =>
        {
            status.LastResortKyberPrekey.Signature = ByteString.CopyFromUtf8("mismatch");
            return status;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => bob.SynchronizePrekeysAsync());
        Assert.Single(pair.BobEndpoint.Rotations);
    }

    [Fact]
    public async Task RegistrationRetryDoesNotReplacePublishedIdentityAndLegacyBundleIsRefused()
    {
        using var pair = await Pair.CreateAsync(synchronize: false);
        var count = 0;
        pair.AliceEndpoint.AfterRegister = () => { count++; throw new IOException(); };
        await Assert.ThrowsAsync<IOException>(() => pair.Alice.SynchronizePrekeysAsync());
        var registration = pair.AliceEndpoint.Registration!.Clone();
        await pair.OpenAlice().SynchronizePrekeysAsync();
        Assert.Equal(1, count);
        Assert.Equal(registration, pair.AliceEndpoint.Registration);

        await pair.Bob.SynchronizePrekeysAsync();
        pair.AliceEndpoint.MapBundle = bundle => { bundle.KyberPrekey = null; return bundle; };
        var before = pair.AliceDatabase.Store.ReadSecretSnapshot(pair.AliceScope);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "refused"));
        Assert.Equal(before.Revision, pair.AliceDatabase.Store.ReadSecretSnapshot(pair.AliceScope).Revision);
        Assert.Empty(pair.BobEndpoint.Invites);
    }

    [Theory]
    [InlineData(true, SignalError.UnsupportedSnapshot)]
    [InlineData(false, SignalError.InvalidInput)]
    public async Task InvalidSnapshotIsReportedAndPreserved(bool unsupported, SignalError expected)
    {
        using var pair = await Pair.CreateAsync();
        var store = pair.AliceDatabase.Store;
        var before = store.ReadSecretSnapshot(pair.AliceScope);
        byte[] invalid = unsupported
            ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(before.State).Replace("\"version\":1", "\"version\":99"))
            : [0xff, 0x00];
        var saved = store.CommitSecret(pair.AliceScope, before, invalid, before.Generation, _ => { });
        var failure = await Assert.ThrowsAsync<SignalProtocolException>(() => pair.OpenAlice().SynchronizePrekeysAsync());
        Assert.Equal(expected, failure.Error);
        Assert.Equal(saved.Revision, store.ReadSecretSnapshot(pair.AliceScope).Revision);
        Assert.Equal(invalid, store.ReadSecretSnapshot(pair.AliceScope).State);
    }

    [Fact]
    public async Task IdentityChangeIsDurableAndBlocksSendingWithoutResettingRatchet()
    {
        using var pair = await Pair.CreateAsync();
        var (_, bob) = await pair.ConnectAsync();
        var before = pair.BobDatabase.Store.ReadSecretSnapshot(pair.BobScope);
        var response = await pair.AliceEndpoint.FetchPrekeyBundleAsync(new()
        { UserId = 2, DeviceId = pair.BobEndpoint.Id, RequirePqxdh = true }, default);
        var bundle = response.Bundle;
        var replacement = SignalProtocol.CreateDevice();
        var session = SignalProtocol.ProcessBundle(replacement.State, replacement.Generation, pair.BobEndpoint.Id,
            new SignalPreKeyBundle(bundle.RegistrationId, bundle.IdentityPubkey.ToByteArray(),
                new(bundle.OneTimePrekey.PrekeyId, bundle.OneTimePrekey.PublicKey.ToByteArray()),
                new(bundle.SignedPrekey.PrekeyId, bundle.SignedPrekey.PublicKey.ToByteArray(), bundle.SignedPrekey.Signature.ToByteArray()),
                new(bundle.KyberPrekey.PrekeyId, bundle.KyberPrekey.PublicKey.ToByteArray(), bundle.KyberPrekey.Signature.ToByteArray())));
        var ciphertext = SignalProtocol.Encrypt(session.State, session.Generation, pair.BobEndpoint.Id, "changed"u8.ToArray());
        var delivery = (await pair.AliceEndpoint.SendSecretChatInviteAsync(new()
        {
            RecipientUserId = 2, RecipientDeviceId = pair.BobEndpoint.Id,
            InitialEnvelope = ByteString.CopyFrom(ciphertext.Output.Ciphertext)
        }, default)).InviteId;
        Assert.NotEmpty(delivery);
        var alerts = new List<SecretChatInfo>();
        pair.Bob.PeerIdentityChanged += alerts.Add;
        var error = await Assert.ThrowsAsync<SignalProtocolException>(() =>
            pair.Bob.AcceptInviteAsync(pair.BobEndpoint.Invites.Last()));
        Assert.Equal(SignalError.UntrustedIdentity, error.Error);
        Assert.Equal(bob.Id, Assert.Single(alerts).Id);
        Assert.Equal(SecretChatStatus.KeyChanged, Assert.Single(await pair.OpenBob().ListChatsAsync()).Status);
        Assert.Equal(before.State, pair.BobDatabase.Store.ReadSecretSnapshot(pair.BobScope).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pair.Bob.SendMessageAsync(bob.Id, "blocked"));
    }

    private sealed class Pair : IDisposable
    {
        internal readonly ChatDraftTests.DraftDatabase AliceDatabase;
        internal readonly ChatDraftTests.DraftDatabase BobDatabase;
        internal readonly SecretChatTestServer.Endpoint AliceEndpoint;
        internal readonly SecretChatTestServer.Endpoint BobEndpoint;
        internal FakeTimeProvider Clock = new(DateTimeOffset.UtcNow);
        internal readonly SecretChatService Alice;
        internal readonly SecretChatService Bob;

        private Pair(ChatDraftTests.DraftDatabase alice, ChatDraftTests.DraftDatabase bob)
        {
            AliceDatabase = alice;
            BobDatabase = bob;
            var server = new SecretChatTestServer();
            AliceEndpoint = server.AddDevice(1, "alice-device");
            BobEndpoint = server.AddDevice(2, "bob-device");
            Alice = OpenAlice();
            Bob = OpenBob();
        }

        internal SecretScope AliceScope => SecretScope.Create(Node, 1);
        internal SecretScope BobScope => SecretScope.Create(Node, 2);
        internal SecretChatService OpenAlice() => new(AliceDatabase.Store, AliceEndpoint, Node, 1, () => true, Clock);
        internal SecretChatService OpenBob() => new(BobDatabase.Store, BobEndpoint, Node, 2, () => true, Clock);

        internal static async Task<Pair> CreateAsync(bool synchronize = true)
        {
            var pair = new Pair(await ChatDraftTests.DraftDatabase.CreateAsync(), await ChatDraftTests.DraftDatabase.CreateAsync());
            try
            {
                if (synchronize)
                    await Task.WhenAll(pair.Alice.SynchronizePrekeysAsync(), pair.Bob.SynchronizePrekeysAsync());
                return pair;
            }
            catch
            {
                pair.Dispose();
                throw;
            }
        }

        internal async Task<(SecretChatInfo Alice, SecretChatInfo Bob)> ConnectAsync()
        {
            var alice = await Alice.CreateChatAsync(2, BobEndpoint.Id, "initial");
            var bob = await Bob.AcceptInviteAsync(BobEndpoint.Invites.Last());
            await Alice.ApplyResolutionAsync(AliceEndpoint.Resolutions.Last());
            return (alice, bob);
        }

        public void Dispose()
        {
            AliceDatabase.Dispose();
            BobDatabase.Dispose();
        }
    }
}
