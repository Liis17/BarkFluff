using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text;
using System.Collections.Specialized;
using System.ComponentModel;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Updates;
using Grpc.Core;
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

    [Fact]
    public async Task TwoClientsExchangeViaStreamsAndRestoreHistoryAfterAckFailure()
    {
        using var pair = await Pair.CreateAsync();
        using var lifetime = new CancellationTokenSource();
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var alice = new SecretChatsViewModel(ui, new StubLocalizationService());
        var bob = new SecretChatsViewModel(ui, new StubLocalizationService());
        var notifications = new List<string>();
        ui.Post(() =>
        {
            alice.Start(pair.Alice, pair.AliceEndpoint.OpenStreams(), lifetime.Token, _ => { });
            bob.Start(pair.Bob, pair.BobEndpoint.OpenStreams(), lifetime.Token, notifications.Add);
        });
        try
        {
            await UntilAsync(ui, alice, () => alice.IsAvailable);
            await UntilAsync(ui, bob, () => bob.IsAvailable);
            await ui.RunAsync(() => alice.CreateAsync(2, "Bob", pair.BobEndpoint.Id, "invite"));
            await UntilAsync(ui, bob, () => bob.Invites.Count == 1);
            ui.Post(() => Assert.Equal("alice-de", bob.Invites.Single().DevicePrefix));
            await ui.RunAsync(() => bob.AcceptCommand.ExecuteAsync(bob.Invites.Single()));
            await UntilAsync(ui, alice, () => alice.CanSend);
            Assert.True(bob.CanSend);

            var firstAck = 0;
            pair.BobEndpoint.BeforeAck = _ =>
            {
                if (Interlocked.Increment(ref firstAck) == 1)
                    throw new IOException();
            };
            await ui.RunAsync(() =>
            {
                alice.Draft = "Alice message";
                return alice.SendCommand.ExecuteAsync(null);
            });
            await UntilAsync(ui, bob, () => bob.Messages.Any(message => message.Text == "Alice message"));
            var original = pair.BobEndpoint.Messages.Single();
            Assert.True((await pair.Bob.ReceiveMessageAsync(original)).Duplicate);

            // Потерянный ответ произошёл после публикации, поэтому автоматический повтор недопустим.
            var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.BobEndpoint.AfterSend = () =>
            {
                sending.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                throw new IOException();
            };
            var send = ui.RunAsync(() =>
            {
                bob.Draft = "Bob message";
                return bob.SendCommand.ExecuteAsync(null);
            });
            try
            {
                await sending.Task.WaitAsync(TimeSpan.FromSeconds(8));
                ui.Post(() =>
                {
                    Assert.False(bob.CanSend);
                    Assert.Contains(bob.Messages, row => row.Delivery == SecretDeliveryState.Sending);
                });
            }
            finally { release.TrySetResult(); }
            await send;
            await UntilAsync(ui, alice, () => alice.Messages.Any(message => message.Text == "Bob message"));
            ui.Post(() =>
            {
                Assert.Single(bob.Messages, row => row.Text == "Alice message");
                Assert.Single(notifications);
                var sent = Assert.Single(alice.Messages, row => row.Text == "Alice message" && row.Delivery == SecretDeliveryState.Sent);
                Assert.StartsWith("Secret_Sent · ", sent.Caption);
                Assert.Contains(bob.Messages, row => row.Text == "Bob message" && row.CanDelete);
            });
            await pair.Bob.RetryPendingOperationsAsync();
            Assert.Single(pair.AliceEndpoint.Messages);
        }
        finally
        {
            await ui.RunAsync(() => Task.WhenAll(alice.Stop(), bob.Stop()));
        }
    }

    [Fact]
    public async Task NewInvitationWithChangedIdentityKeepsTheRatchetAndBlocksTheComposer()
    {
        using var pair = await Pair.CreateAsync();
        using var replacement = await Pair.CreateAsync();
        var (chat, _) = await pair.ConnectAsync();
        await pair.Alice.ForgetChatAsync(chat.Id);
        var fresh = (await replacement.AliceEndpoint.FetchPrekeyBundleAsync(new()
        {
            UserId = 2, DeviceId = pair.BobEndpoint.Id, RequirePqxdh = true
        }, default)).Bundle;
        pair.AliceEndpoint.MapBundle = _ => fresh;
        var snapshot = pair.AliceDatabase.Store.ReadSecretSnapshot(pair.AliceScope);

        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = new SecretChatsViewModel(ui, new StubLocalizationService());
        Assert.Equal(SignalError.UntrustedIdentity, (await Assert.ThrowsAsync<SignalProtocolException>(() =>
            pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "blocked"))).Error);
        Assert.Equal(snapshot.State, pair.AliceDatabase.Store.ReadSecretSnapshot(pair.AliceScope).State);
        var changed = Assert.Single(await pair.Alice.ListChatsAsync());
        Assert.Equal(SecretChatStatus.KeyChanged, changed.Status);
        Assert.Single(pair.BobEndpoint.Invites);

        ui.Post(() => vm.Start(pair.Alice, pair.AliceEndpoint.OpenStreams(), default, _ => { }));
        try
        {
            await UntilAsync(ui, vm, () => vm.IsAvailable);
            // Create also reloads the local chat list when the peer is already blocked.
            await ui.RunAsync(() => vm.CreateAsync(2, "Bob", pair.BobEndpoint.Id, "still blocked"));
            ui.Post(() =>
            {
                Assert.True(vm.OpenChat(changed.Id));
                Assert.Equal("Secret_KeyChanged", vm.Status);
                Assert.False(vm.CanSend);
            });
        }
        finally { await ui.RunAsync(vm.Stop); }
    }

    [Fact]
    public void SecretNotificationContainsOnlyTheLocalizedGenericText()
    {
        var presenter = new SecretNotificationPresenter();
        var service = new MessageNotificationService(presenter, new StubLocalizationService());
        var target = new MessageNotificationTarget(Node, 1, "secret:local-id");
        service.ShowSecret(target);
        Assert.Equal(new MessageNotification(target, "BarkFluff", "Secret_Notification"), presenter.Notification);
    }

    private sealed class SecretNotificationPresenter : INotificationPresenter
    {
        internal MessageNotification? Notification { get; private set; }
        public void Show(MessageNotification notification) => Notification = notification;
        public void PreviewSound(ChatNotificationSound sound, string text) { }
        public void RemoveChat(MessageNotificationTarget target) { }
        public void RemoveAll() { }
        public void RemoveReply(NotificationReply reply) { }
    }

    [Fact]
    public async Task RejectedInviteStaysHiddenAfterRestart()
    {
        using var pair = await Pair.CreateAsync();
        await pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "decline");
        var invite = Assert.Single(pair.BobEndpoint.Invites);
        Assert.True(await pair.Bob.ShouldOfferInviteAsync(invite, default));
        await pair.Bob.RejectInviteAsync(invite.InviteId);
        Assert.False(await pair.OpenBob().ShouldOfferInviteAsync(invite, default));
    }

    [Fact]
    public async Task MalformedEnvelopeHasBoundedDecryptionsAcrossRestart()
    {
        using var pair = await Pair.CreateAsync();
        var envelope = new SecretEnvelope
        {
            MessageId = "malformed", SenderUserId = 1, SenderDeviceId = pair.AliceEndpoint.Id,
            RecipientDeviceId = pair.BobEndpoint.Id, Envelope = ByteString.CopyFrom([1, 2, 3])
        };
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.True((await pair.OpenBob().ReceiveMessageAsync(envelope)).Ignored);
        pair.Clock.Advance(TimeSpan.FromMinutes(1));
        var revision = pair.BobDatabase.Store.ReadSecretSnapshot(pair.BobScope).Revision;
        Assert.True((await pair.OpenBob().ReceiveMessageAsync(envelope)).Ignored);
        Assert.Equal(revision, pair.BobDatabase.Store.ReadSecretSnapshot(pair.BobScope).Revision);
        Assert.Empty(pair.BobEndpoint.Acks);
    }

    [Fact]
    public async Task ReplayedSignalCiphertextUnderAnotherIdIsNeverAcknowledged()
    {
        using var pair = await Pair.CreateAsync();
        var (alice, bob) = await pair.ConnectAsync();
        await pair.Bob.SendMessageAsync(bob.Id, "ratchet");
        await pair.Alice.ReceiveMessageAsync(pair.AliceEndpoint.Messages.Single());
        await pair.Alice.SendMessageAsync(alice.Id, "ordinary Signal message");
        var received = pair.BobEndpoint.Messages.Single();
        Assert.NotNull((await pair.Bob.ReceiveMessageAsync(received)).Message);
        var replay = received.Clone();
        replay.MessageId = "not-the-original-id";
        Assert.True((await pair.Bob.ReceiveMessageAsync(replay)).Ignored);
        var revision = pair.BobDatabase.Store.ReadSecretSnapshot(pair.BobScope).Revision;
        Assert.True((await pair.OpenBob().ReceiveMessageAsync(replay)).Ignored);
        Assert.Equal(revision, pair.BobDatabase.Store.ReadSecretSnapshot(pair.BobScope).Revision);
        Assert.Equal(received.MessageId, Assert.Single(pair.BobEndpoint.Acks));
    }

    [Fact]
    public async Task SignalBeforePrekeySurvivesFastRedeliveries()
    {
        using var pair = await Pair.CreateAsync();
        var (prekey, signal) = await MakePrekeyAndSignalAsync(pair);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.True((await pair.OpenBob().ReceiveMessageAsync(signal)).Ignored);

        Assert.NotNull((await pair.Bob.ReceiveMessageAsync(prekey)).Message);
        Assert.Equal("after prekey", (await pair.OpenBob().ReceiveMessageAsync(signal)).Message?.Text);
    }

    [Fact]
    public async Task SignalBeforeDelayedPrekeyDecryptsOnRedelivery()
    {
        using var pair = await Pair.CreateAsync();
        var (prekey, signal) = await MakePrekeyAndSignalAsync(pair);
        Assert.True((await pair.Bob.ReceiveMessageAsync(signal)).Ignored);
        pair.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.NotNull((await pair.Bob.ReceiveMessageAsync(prekey)).Message);
        Assert.Equal("after prekey", (await pair.OpenBob().ReceiveMessageAsync(signal)).Message?.Text);
        Assert.Equal(new[] { prekey.MessageId, signal.MessageId }, pair.BobEndpoint.Acks);
    }

    [Fact]
    public async Task LocalCommitFailureLeavesEnvelopeRetryable()
    {
        using var pair = await Pair.CreateAsync();
        var (prekey, _) = await MakePrekeyAndSignalAsync(pair);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={pair.BobDatabase.Paths.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_history_insert BEFORE INSERT ON secret_history
                BEGIN SELECT RAISE(ABORT, 'simulated local write failure'); END;
                """;
            command.ExecuteNonQuery();
            try
            {
                await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
                    pair.Bob.ReceiveMessageAsync(prekey, default, keepStreamOnAckFailure: true));
                Assert.Empty(pair.BobEndpoint.Acks);
                Assert.Empty(pair.BobDatabase.Store.ReadSecrets<JsonObject>(pair.BobScope, SecretTable.FailedReplay));
                pair.Clock.Advance(TimeSpan.FromSeconds(61));

                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var connections = 0;
                var drained = false;
                var availability = new List<bool>();
                async IAsyncEnumerable<NewSecretMessageEvent> Read([EnumeratorCancellation] CancellationToken token)
                {
                    var attempt = ++connections;
                    if (attempt == 2)
                    {
                        command.CommandText = "DROP TRIGGER IF EXISTS fail_history_insert;";
                        command.ExecuteNonQuery();
                    }
                    yield return new() { Envelope = prekey };
                    drained = attempt == 2;
                    stop.Cancel();
                    await Task.CompletedTask;
                }

                var streams = new SecretChatStreams(pair.BobEndpoint.InviteStream.Reader.ReadAllAsync,
                    pair.BobEndpoint.ResolutionStream.Reader.ReadAllAsync, Read);
                await streams.RunAsync(pair.Bob, availability.Add, _ => { }, () => Task.CompletedTask, stop.Token);
                Assert.True(drained);
                Assert.Equal(2, connections);
                Assert.Equal(new[] { true, false, true }, availability);
            }
            finally
            {
                command.CommandText = "DROP TRIGGER IF EXISTS fail_history_insert;";
                command.ExecuteNonQuery();
            }
        }
        Assert.Equal("first prekey", Assert.Single(pair.BobDatabase.Store
            .ReadSecrets<SecretChatMessage>(pair.BobScope, SecretTable.History)).Text);
        Assert.Equal(prekey.MessageId, Assert.Single(pair.BobEndpoint.Acks));
    }

    [Fact]
    public async Task FailedReplayEntriesAreBoundedAndExpireWithoutRemovingSuccessfulEntries()
    {
        using var pair = await Pair.CreateAsync();
        var (prekey, _) = await MakePrekeyAndSignalAsync(pair);
        Assert.NotNull((await pair.Bob.ReceiveMessageAsync(prekey)).Message);
        var malformed = prekey.Clone();
        malformed.Envelope = ByteString.CopyFrom([1, 2, 3]);
        for (var index = 0; index < 130; index++)
        {
            malformed.MessageId = $"bad-{index}";
            Assert.True((await pair.Bob.ReceiveMessageAsync(malformed)).Ignored);
        }

        IReadOnlyList<JsonObject> FailedEntries() =>
            pair.BobDatabase.Store.ReadSecrets<JsonObject>(pair.BobScope, SecretTable.FailedReplay);

        Assert.Equal(128, FailedEntries().Count);
        pair.Clock.Advance(TimeSpan.FromDays(1));
        await pair.OpenBob().RetryPendingOperationsAsync();
        Assert.Empty(FailedEntries());
        Assert.True((await pair.Bob.ReceiveMessageAsync(prekey)).Duplicate);
    }

    [Fact]
    public async Task InvalidEnvelopeDoesNotRestartSubscriptionAndIsTracked()
    {
        using var pair = await Pair.CreateAsync();
        var (prekey, _) = await MakePrekeyAndSignalAsync(pair);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var drained = false;
        async IAsyncEnumerable<NewSecretMessageEvent> Read([EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            var invalid = prekey.Clone();
            invalid.MessageId = "invalid-recipient";
            invalid.RecipientDeviceId = "other-device";
            yield return new() { Envelope = invalid };
            yield return new() { Envelope = prekey };
            drained = true;
            stop.Cancel();
        }

        var streams = new SecretChatStreams(pair.BobEndpoint.InviteStream.Reader.ReadAllAsync,
            pair.BobEndpoint.ResolutionStream.Reader.ReadAllAsync, Read);
        await streams.RunAsync(pair.Bob, _ => { }, _ => { }, () => Task.CompletedTask, stop.Token);
        Assert.True(drained);
        Assert.Equal(prekey.MessageId, Assert.Single(pair.BobEndpoint.Acks));
        Assert.Contains(pair.BobDatabase.Store.ReadSecrets<JsonObject>(pair.BobScope, SecretTable.FailedReplay),
            entry => entry["Key"]!.GetValue<string>().EndsWith(":invalid-recipient", StringComparison.Ordinal) &&
                entry["FailedDecryptions"]?.GetValue<int>() == 1);
    }

    [Fact]
    public async Task PrekeySyncDoesNotHoldTheMessageQueueDuringRpc()
    {
        using var pair = await Pair.CreateAsync();
        var (prekey, signal) = await MakePrekeyAndSignalAsync(pair);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var syncing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statusCalls = 0;
        pair.BobEndpoint.MapStatus = status =>
        {
            if (Interlocked.Increment(ref statusCalls) == 2)
            {
                syncing.SetResult();
                release.Task.GetAwaiter().GetResult();
            }
            return status;
        };
        async IAsyncEnumerable<NewSecretMessageEvent> Read([EnumeratorCancellation] CancellationToken token)
        {
            yield return new() { Envelope = prekey };
            await syncing.Task.WaitAsync(token);
            yield return new() { Envelope = signal };
            processed.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }

        var streams = new SecretChatStreams(pair.BobEndpoint.InviteStream.Reader.ReadAllAsync,
            pair.BobEndpoint.ResolutionStream.Reader.ReadAllAsync, Read);
        var run = streams.RunAsync(pair.Bob, _ => { }, _ => { }, () => Task.CompletedTask, stop.Token);
        try
        {
            await processed.Task.WaitAsync(stop.Token);
            Assert.Equal(new[] { prekey.MessageId, signal.MessageId }, pair.BobEndpoint.Acks);
        }
        finally
        {
            stop.Cancel();
            release.TrySetResult();
            await run;
        }
    }

    [Theory]
    [InlineData("register")]
    [InlineData("rotate")]
    [InlineData("replenish")]
    public async Task ReceiveCanAdvanceGenerationWhilePrekeyPublicationIsBlocked(string publication)
    {
        using var pair = await Pair.CreateAsync(synchronize: publication != "register");
        if (publication == "register")
            await pair.Alice.SynchronizePrekeysAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Block()
        {
            entered.SetResult();
            release.Task.WaitAsync(timeout.Token).GetAwaiter().GetResult();
        }

        switch (publication)
        {
            case "register":
                pair.BobEndpoint.AfterRegister = Block;
                break;
            case "rotate":
                pair.Clock.Advance(TimeSpan.FromDays(8));
                pair.BobEndpoint.AfterRotate = Block;
                break;
            case "replenish":
                pair.BobEndpoint.Ec.Clear();
                pair.BobEndpoint.AfterReplenish = Block;
                break;
        }

        ulong Generation() => pair.BobDatabase.Store.ReadSecretSnapshot(pair.BobScope).Generation;
        var initial = Generation();
        var sync = pair.Bob.SynchronizePrekeysAsync(timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            var before = Generation();
            Assert.True(before > initial);
            var (prekey, signal) = await MakePrekeyAndSignalAsync(pair);
            Assert.Equal("first prekey", (await pair.Bob.ReceiveMessageAsync(prekey, timeout.Token)).Message?.Text);
            var afterPrekey = Generation();
            Assert.True(afterPrekey > before);
            Assert.Equal("after prekey", (await pair.Bob.ReceiveMessageAsync(signal, timeout.Token)).Message?.Text);
            var afterSignal = Generation();
            Assert.True(afterSignal > afterPrekey);
            Assert.Equal(new[] { prekey.MessageId, signal.MessageId }, pair.BobEndpoint.Acks);
            Assert.False(sync.IsCompleted);

            release.SetResult();
            await sync.WaitAsync(timeout.Token);
            Assert.True(Generation() >= afterSignal);

            var bob = await pair.Bob.AcceptInviteAsync(Assert.Single(pair.BobEndpoint.Invites), timeout.Token);
            var alice = Assert.Single(await pair.Alice.ListChatsAsync());
            await pair.Alice.ApplyResolutionAsync(Assert.Single(pair.AliceEndpoint.Resolutions), timeout.Token);
            await pair.Bob.SendMessageAsync(bob.Id, "reply after sync", timeout.Token);
            Assert.Equal("reply after sync", (await pair.Alice.ReceiveMessageAsync(
                Assert.Single(pair.AliceEndpoint.Messages), timeout.Token)).Message?.Text);
            await pair.Alice.SendMessageAsync(alice.Id, "next after sync", timeout.Token);
            var beforeNext = Generation();
            Assert.Equal("next after sync", (await pair.Bob.ReceiveMessageAsync(
                Assert.Single(pair.BobEndpoint.Messages), timeout.Token)).Message?.Text);
            Assert.True(Generation() > beforeNext);
        }
        finally
        {
            release.TrySetResult();
            await sync;
        }
    }

    [Fact]
    public async Task ReconnectSubscribesEvenWhenPrekeySyncFails()
    {
        using var pair = await Pair.CreateAsync();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = 0;
        var statusCalls = 0;
        pair.BobEndpoint.MapStatus = status =>
        {
            if (Interlocked.Increment(ref statusCalls) == 1)
                return status;
            failed.TrySetResult();
            throw new IOException();
        };
        async IAsyncEnumerable<NewSecretMessageEvent> Read([EnumeratorCancellation] CancellationToken token)
        {
            if (Interlocked.Increment(ref connections) == 1)
                yield break;
            reconnected.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }

        var streams = new SecretChatStreams(pair.BobEndpoint.InviteStream.Reader.ReadAllAsync,
            pair.BobEndpoint.ResolutionStream.Reader.ReadAllAsync, Read);
        var run = streams.RunAsync(pair.Bob, _ => { }, _ => { }, () => Task.CompletedTask, stop.Token);
        try
        {
            await Task.WhenAll(reconnected.Task, failed.Task).WaitAsync(stop.Token);
            Assert.Equal(2, Volatile.Read(ref connections));
        }
        finally
        {
            stop.Cancel();
            await run;
        }
    }

    [Fact]
    public async Task MessageBurstRefreshesHistoryOnce()
    {
        using var pair = await Pair.CreateAsync();
        var (alice, _) = await pair.ConnectAsync();
        for (var index = 0; index < 3; index++)
            await pair.Alice.SendMessageAsync(alice.Id, $"burst-{index}");

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var clock = new RefreshClock();
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        async IAsyncEnumerable<NewSecretMessageEvent> Read([EnumeratorCancellation] CancellationToken token)
        {
            foreach (var envelope in pair.BobEndpoint.Messages)
                yield return new() { Envelope = envelope };
            await clock.TimerCreated.Task.WaitAsync(token);
            clock.Advance(TimeSpan.FromMilliseconds(50));
            await refreshed.Task.WaitAsync(token);
            stop.Cancel();
        }

        var streams = new SecretChatStreams(pair.BobEndpoint.InviteStream.Reader.ReadAllAsync,
            pair.BobEndpoint.ResolutionStream.Reader.ReadAllAsync, Read, clock);
        await streams.RunAsync(pair.Bob, _ => { }, _ => { }, () =>
        {
            if (Interlocked.Increment(ref count) == 2)
                refreshed.TrySetResult();
            return Task.CompletedTask;
        }, stop.Token);
        Assert.True(refreshed.Task.IsCompletedSuccessfully);
        Assert.Equal(2, count); // Начальная загрузка и одно обновление для серии.
        Assert.Equal(3, pair.BobEndpoint.Acks.Count);
    }

    private sealed class RefreshClock : FakeTimeProvider
    {
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }
    }

    [Fact]
    public async Task SecretDraftDebouncesAndFlushesOnCloseAndStop()
    {
        using var pair = await Pair.CreateAsync();
        var (_, chat) = await pair.ConnectAsync();
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = new SecretChatsViewModel(ui, new StubLocalizationService(), pair.Clock);
        using var lifetime = new CancellationTokenSource();
        ui.Post(() => vm.Start(pair.Bob, pair.BobEndpoint.OpenStreams(), lifetime.Token, _ => { }));
        try
        {
            await UntilAsync(ui, vm, () => vm.Chats.Count == 1 || vm.IsAvailable);
            // Первичное обновление списка отслеживается отдельно от флага доступности.
            await ui.RunAsync(async () =>
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Check(object? sender, NotifyCollectionChangedEventArgs args) => ready.TrySetResult();
                vm.Chats.CollectionChanged += Check;
                try
                {
                    if (vm.Chats.Count == 0)
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
                    Assert.True(vm.OpenChat(chat.Id));
                    vm.Draft = "a";
                    pair.Clock.Advance(TimeSpan.FromSeconds(1));
                    vm.Draft = "ab";
                    pair.Clock.Advance(TimeSpan.FromSeconds(1));
                    Assert.Equal("", await pair.Bob.ReadDraftAsync(chat.Id, default));
                    vm.Close();
                    await vm.FlushDraftAsync();
                    Assert.Equal("ab", await pair.Bob.ReadDraftAsync(chat.Id, default));

                    Assert.True(vm.OpenChat(chat.Id));
                    vm.Draft = "latest";
                    await vm.Stop();
                    Assert.Equal("latest", await pair.Bob.ReadDraftAsync(chat.Id, default));
                }
                finally { vm.Chats.CollectionChanged -= Check; }
            });
        }
        finally { await ui.RunAsync(vm.Stop); }
    }

    private static async Task<(SecretEnvelope Prekey, SecretEnvelope Signal)> MakePrekeyAndSignalAsync(Pair pair)
    {
        await pair.Alice.CreateChatAsync(2, pair.BobEndpoint.Id, "first prekey");
        var prekey = new SecretEnvelope
        {
            MessageId = "prekey", SenderUserId = 1, SenderDeviceId = pair.AliceEndpoint.Id,
            RecipientDeviceId = pair.BobEndpoint.Id, Envelope = pair.BobEndpoint.Invites.Single().InitialEnvelope
        };
        var alice = pair.AliceDatabase.Store.ReadSecretSnapshot(pair.AliceScope);
        var after = SignalProtocol.Encrypt(alice.State, alice.Generation, pair.BobEndpoint.Id, "after prekey"u8.ToArray());
        Assert.Equal(SignalMessageKind.PreKey, after.Output.Kind);
        // В PreKeySignalMessage поле 4 содержит обычное Signal-сообщение той же цепочки.
        using var wrapper = new CodedInputStream(after.Output.Ciphertext[1..]);
        var signal = prekey.Clone();
        signal.MessageId = "signal";
        for (var tag = wrapper.ReadTag(); tag != 0; tag = wrapper.ReadTag())
        {
            if (tag == WireFormat.MakeTag(4, WireFormat.WireType.LengthDelimited))
            {
                signal.Envelope = wrapper.ReadBytes();
                break;
            }
            wrapper.SkipLastField();
        }
        Assert.NotEqual(prekey.Envelope, signal.Envelope);
        return (prekey, signal);
    }

    [Fact]
    public async Task UnimplementedStreamStopsAllSubscriptions()
    {
        using var pair = await Pair.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var available = new List<bool>();
        var connections = 0;
        IAsyncEnumerable<SecretChatInviteEvent> Unsupported(CancellationToken token)
        {
            Interlocked.Increment(ref connections);
            throw new RpcException(new Status(StatusCode.Unimplemented, ""));
        }
        var streams = new SecretChatStreams(Unsupported, pair.BobEndpoint.ResolutionStream.Reader.ReadAllAsync,
            pair.BobEndpoint.MessageStream.Reader.ReadAllAsync);
        await streams.RunAsync(pair.Bob, available.Add, _ => { }, () => Task.CompletedTask, timeout.Token);
        Assert.False(timeout.IsCancellationRequested);
        Assert.Equal(1, connections);
        Assert.False(available.Last());
    }

    private static async Task UntilAsync(InlineUiDispatcher ui, SecretChatsViewModel vm, Func<bool> condition)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check() { if (condition()) ready.TrySetResult(); }
        PropertyChangedEventHandler property = (_, _) => Check();
        NotifyCollectionChangedEventHandler invites = (_, _) => Check();
        ui.Post(() =>
        {
            vm.PropertyChanged += property;
            vm.Invites.CollectionChanged += invites;
            Check();
        });
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        finally
        {
            ui.Post(() =>
            {
                vm.PropertyChanged -= property;
                vm.Invites.CollectionChanged -= invites;
            });
        }
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
