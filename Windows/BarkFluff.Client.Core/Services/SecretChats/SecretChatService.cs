using System.Security.Cryptography;
using System.Text;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;
using BarkFluff.Proto.Users;
using Google.Protobuf;
using static BarkFluff.Client.Core.Infrastructure.Storage.SqliteApplicationDataStore;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services.SecretChats;

public enum SecretChatStatus { Pending, Accepted, KeyChanged }

public sealed record SecretChatInfo(string Id, long PeerUserId, string PeerDeviceId, SecretChatStatus Status);
public sealed record SecretChatMessage(string Id, string? ChatId, long SenderUserId, string SenderDeviceId,
    DateTimeOffset SentAt, string Text, bool Outgoing = false, string? ServerId = null);
public sealed record SecretMessageDelivery(SecretChatMessage? Message, bool Duplicate, bool Ignored = false)
{
    internal bool ConsumedPreKey { get; init; }
}

public enum SecretDeliveryState { Sending, Sent, Failed }

public sealed partial class SecretChatService
{
    private const int MaxFailedReplayEntries = 128;
    private static readonly TimeSpan DecryptionRetryWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FailedReplayRetention = TimeSpan.FromDays(1);

    private sealed record Peer(long UserId, string DeviceId);
    private sealed record Chat(SecretChatInfo Info, string[] OutgoingInvites, string[] PendingAccepts,
        DateTimeOffset ExpiresAt = default, byte[]? PendingEnvelope = null, SecretChatMessage? Initial = null, string? Draft = null);
    private sealed record Replay(string Key, string HistoryId, string? PendingAck = null, string? ChatId = null,
        int FailedDecryptions = 0, bool PendingRejection = false, DateTimeOffset? FirstFailureAt = null, bool TerminalFailure = false);

    private readonly SqliteApplicationDataStore _store;
    private readonly ISecretChatGateway _gateway;
    private readonly SecretScope _scope;
    private readonly SecretScopeLock _scopeLock;
    private readonly int _epoch;
    private readonly Func<bool> _isCurrent;
    private readonly TimeProvider _clock;

    internal SecretChatService(SqliteApplicationDataStore store, ISecretChatGateway gateway,
        string node, long userId, Func<bool> isCurrent, TimeProvider? clock = null)
    {
        _store = store;
        _gateway = gateway;
        _scope = SecretScope.Create(node, userId);
        _scopeLock = store.GetSecretScopeLock(_scope);
        _epoch = _scopeLock.Epoch;
        _isCurrent = isCurrent;
        _clock = clock ?? TimeProvider.System;
    }

    public static SecretChatService Open(SqliteApplicationDataStore store, WebApiClient api, IClientSession session)
    {
        var connection = session.CurrentConnection ?? throw new InvalidOperationException("The node session is unavailable.");
        var parameters = connection.ConnectionParameters;
        var userId = parameters.UserId;
        if (userId <= 0)
            throw new InvalidOperationException("The user session is unavailable.");
        return new(store, new SecretChatGateway(api, parameters), connection.Profile.BeaconAddress, userId,
            () => ReferenceEquals(session.CurrentConnection, connection) && parameters.UserId == userId);
    }

    public event Action<SecretChatInfo>? PeerIdentityChanged;
    internal event Action<string>? IncomingMessageStored;

    internal Task<bool> ShouldOfferInviteAsync(SecretChatInviteEvent invite, CancellationToken cancellationToken) =>
        RunAsync(_ => Task.FromResult(!string.IsNullOrWhiteSpace(invite.InviteId) &&
            invite.SenderUserId > 0 && !string.IsNullOrWhiteSpace(invite.SenderDeviceId) &&
            !invite.InitialEnvelope.IsEmpty &&
            (invite.ExpiresAt is null || invite.ExpiresAt.ToDateTimeOffset() > _clock.GetUtcNow()) &&
            !HasInviteDecision(invite)), cancellationToken);

    private bool HasInviteDecision(SecretChatInviteEvent invite) =>
        Read<Replay>(SecretTable.Replay, "rejected:" + invite.InviteId) is not null ||
        Read<Replay>(SecretTable.Replay,
            "accepted:" + PeerKey(invite.SenderUserId, invite.SenderDeviceId) + ":" + invite.InviteId) is not null;

    public Task<IReadOnlyList<SecretChatInfo>> ListChatsAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<SecretChatInfo>>(_ => Task.FromResult<IReadOnlyList<SecretChatInfo>>(
            Chats().Select(chat => chat.Info).ToList()), cancellationToken);

    public Task<IReadOnlyList<SecretChatMessage>> ReadHistoryAsync(string chatId, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<SecretChatMessage>>(_ => Task.FromResult<IReadOnlyList<SecretChatMessage>>(
            History().Where(message => message.ChatId == chatId)
            .Concat(Read<Chat>(SecretTable.Chats, chatId)?.Initial is { } initial ? [initial] : Array.Empty<SecretChatMessage>())
            .OrderBy(message => message.SentAt).ToList()),
            cancellationToken);

    internal Task<string> ReadDraftAsync(string chatId, CancellationToken token) =>
        RunAsync(_ => Task.FromResult(Read<Chat>(SecretTable.Chats, chatId)?.Draft ?? ""), token);

    internal Task WriteDraftAsync(string chatId, string text, CancellationToken token) =>
        RunAsync(ct =>
        {
            if (Read<Chat>(SecretTable.Chats, chatId) is { } chat && (chat.Draft ?? "") != text)
            {
                var snapshot = _store.ReadSecretSnapshot(_scope);
                Commit(snapshot, snapshot, writer => SaveChat(writer, chat with { Draft = text }), ct);
            }
            return Task.CompletedTask;
        }, token);

    public Task<IReadOnlyList<PeerDeviceInfo>> ListPeerDevicesAsync(long userId, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<PeerDeviceInfo>>(async token =>
            (await RpcAsync(ct => _gateway.ListPeerDevicesAsync(new() { UserId = userId }, ct), token)).Devices
                .Where(device => device.HasBundle && device.SupportsPqxdh).ToList(), cancellationToken);

    public Task<SecretChatInfo> CreateChatAsync(long peerUserId, string peerDeviceId, string text,
        CancellationToken cancellationToken = default)
    {
        var peer = new Peer(peerUserId, peerDeviceId);
        return RunAsync(async token =>
        {
            var keys = RequireKeys();
            if (peer.UserId <= 0 || string.IsNullOrWhiteSpace(peer.DeviceId) || peer.DeviceId == keys.DeviceId)
                throw new ArgumentException("The peer device is invalid.", nameof(peerDeviceId));
            var snapshot = _store.ReadSecretSnapshot(_scope);
            var chat = Find(peer);
            if (chat is not null && (chat.Info.Status != SecretChatStatus.Pending || chat.ExpiresAt > _clock.GetUtcNow()))
            {
                RequireTrusted(chat);
                return (await SendPendingInviteAsync(chat, snapshot, token)).Info;
            }

            var response = await RpcAsync(ct => _gateway.FetchPrekeyBundleAsync(new()
            {
                UserId = peer.UserId, DeviceId = peer.DeviceId, RequirePqxdh = true
            }, ct), token);
            var bundle = DecodeBundle(response.Bundle, peer);
            var session = SignalProtocol.ProcessBundle(snapshot.State, snapshot.Generation, peer.DeviceId, bundle);
            var encrypted = Encrypt(session.State, session.Generation, peer.DeviceId, text);
            if (encrypted.Output.Kind != SignalMessageKind.PreKey)
                throw new SignalProtocolException(SignalError.Protocol);

            var envelope = encrypted.Output.Ciphertext;
            var info = chat?.Info ?? NewChat(peer, SecretChatStatus.Pending);
            var outgoingReplayKey = "out:" + EnvelopeKey(_scope.UserId, keys.DeviceId, envelope);
            var initialId = "init:" + outgoingReplayKey;
            var initial = new SecretChatMessage(initialId, info.Id, _scope.UserId, keys.DeviceId,
                _clock.GetUtcNow(), text, Outgoing: true);
            chat = new Chat(info, chat?.OutgoingInvites ?? [], [], _clock.GetUtcNow().AddDays(1), envelope, initial, chat?.Draft);
            snapshot = Commit(snapshot, Updated(snapshot, encrypted), writer =>
            {
                SaveChat(writer, chat);
                writer.Set(SecretTable.Replay, outgoingReplayKey, new Replay(outgoingReplayKey, initialId));
            }, token);
            return (await SendPendingInviteAsync(chat, snapshot, token)).Info;
        }, cancellationToken, peer);
    }

    public Task<SecretChatInfo> AcceptInviteAsync(SecretChatInviteEvent invite, CancellationToken cancellationToken = default)
    {
        var peer = new Peer(invite.SenderUserId, invite.SenderDeviceId);
        return RunAsync(async token =>
        {
            RequireKeys();
            if (peer.UserId <= 0 || string.IsNullOrWhiteSpace(peer.DeviceId) ||
                string.IsNullOrWhiteSpace(invite.InviteId) || invite.InitialEnvelope.IsEmpty)
                throw new ArgumentException("The secret chat invitation is incomplete.", nameof(invite));
            var snapshot = _store.ReadSecretSnapshot(_scope);
            var chat = Find(peer);
            RequireTrusted(chat);
            var replayKey = EnvelopeKey(peer.UserId, peer.DeviceId, invite.InitialEnvelope.Span);
            if (Read<Replay>(SecretTable.Replay, "rejected:" + invite.InviteId) is not null)
                throw new InvalidOperationException("The secret chat invitation has already been declined.");

            var inviteKey = "accepted:" + PeerKey(peer.UserId, peer.DeviceId) + ":" + invite.InviteId;
            var acceptedInvite = Read<Replay>(SecretTable.Replay, inviteKey);
            if (acceptedInvite is not null)
            {
                if (acceptedInvite.HistoryId != replayKey || chat is null || acceptedInvite.ChatId != chat.Info.Id)
                    throw new InvalidOperationException("The secret chat invitation has already been processed.");
                await AcceptPendingInvitesAsync(chat, snapshot, token);
                return chat.Info;
            }
            var replay = Read<Replay>(SecretTable.Replay, replayKey);
            var existing = replay is null ? null : Read<SecretChatMessage>(SecretTable.History, replay.HistoryId);
            if (replay is not null && (chat is null && (existing is null || existing.ChatId is not null)))
                throw new InvalidOperationException("The secret chat invitation has already been removed.");

            var info = (chat?.Info ?? NewChat(peer, SecretChatStatus.Accepted)) with { Status = SecretChatStatus.Accepted };
            chat = (chat ?? new Chat(info, [], [])) with
            {
                Info = info, PendingAccepts = chat?.PendingAccepts.Append(invite.InviteId).Distinct().ToArray() ?? [invite.InviteId]
            };
            var next = snapshot;
            if (replay is null)
            {
                var decrypted = SignalProtocol.Decrypt(snapshot.State, snapshot.Generation, peer.DeviceId,
                    SignalMessageKind.PreKey, invite.InitialEnvelope.ToByteArray());
                next = Updated(snapshot, decrypted);
                var id = "init:" + replayKey;
                existing = new SecretChatMessage(id, info.Id, peer.UserId, peer.DeviceId,
                    invite.InvitedAt?.ToDateTimeOffset() ?? _clock.GetUtcNow(), DecodeText(decrypted.Output));
            }
            var unattached = UnattachedHistory(peer);
            var retryableFailures = RetryableFailures(peer);
            snapshot = Commit(snapshot, next, writer =>
            {
                ClearFailures(writer, retryableFailures);
                chat = PromoteInitial(writer, chat);
                SaveChat(writer, chat);
                foreach (var message in unattached)
                    SaveMessage(writer, message with { ChatId = info.Id });
                if (existing is not null)
                    SaveMessage(writer, existing with { ChatId = info.Id });
                if (replay is null)
                    writer.Set(SecretTable.Replay, replayKey, new Replay(replayKey, existing!.Id));
                writer.Set(SecretTable.Replay, inviteKey, new Replay(inviteKey, replayKey, ChatId: info.Id));
            }, token);
            await AcceptPendingInvitesAsync(chat, snapshot, token);
            return info;
        }, cancellationToken, peer);
    }

    public Task RejectInviteAsync(string inviteId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(inviteId))
                throw new ArgumentException("The secret chat invitation ID is missing.", nameof(inviteId));
            var key = "rejected:" + inviteId;
            var snapshot = _store.ReadSecretSnapshot(_scope);
            var decision = Read<Replay>(SecretTable.Replay, key);
            if (decision is null)
            {
                decision = new Replay(key, inviteId, PendingRejection: true);
                snapshot = Commit(snapshot, snapshot, writer => writer.Set(SecretTable.Replay, key, decision), token);
            }
            if (decision.PendingRejection)
                await RejectPendingInviteAsync(snapshot, decision, token);
        }, cancellationToken);

    public Task ApplyResolutionAsync(SecretChatInviteResolutionEvent resolution, CancellationToken cancellationToken = default)
    {
        var peer = new Peer(resolution.RecipientUserId, resolution.RecipientDeviceId);
        return RunAsync(token =>
        {
            var snapshot = _store.ReadSecretSnapshot(_scope);
            var chat = Find(peer);
            if (chat is null || !chat.OutgoingInvites.Contains(resolution.InviteId) ||
                chat.Info.Status == SecretChatStatus.KeyChanged)
                return Task.CompletedTask;

            if (!resolution.Accepted)
            {
                if (chat.Info.Status == SecretChatStatus.Pending)
                {
                    var remaining = chat.OutgoingInvites.Where(id => id != resolution.InviteId).ToArray();
                    Commit(snapshot, snapshot, writer =>
                    {
                        if (remaining.Length == 0 && chat.PendingEnvelope is null)
                            writer.Set<Chat>(SecretTable.Chats, chat.Info.Id, null);
                        else
                            SaveChat(writer, chat with { OutgoingInvites = remaining });
                    }, token);
                }
                return Task.CompletedTask;
            }
            var next = snapshot;
            SecretChatMessage? reply = null;
            var replayKey = "resolution:" + resolution.InviteId;
            if (!resolution.ResponseEnvelope.IsEmpty && Read<Replay>(SecretTable.Replay, replayKey) is null)
            {
                var decrypted = Decrypt(snapshot, peer, resolution.ResponseEnvelope, out _);
                next = Updated(snapshot, decrypted);
                reply = new SecretChatMessage(replayKey, chat.Info.Id, peer.UserId, peer.DeviceId, _clock.GetUtcNow(),
                    DecodeText(decrypted.Output));
            }
            var unattached = UnattachedHistory(peer);
            var retryableFailures = RetryableFailures(peer);
            Commit(snapshot, next, writer =>
            {
                ClearFailures(writer, retryableFailures);
                var accepted = chat with { Info = chat.Info with { Status = SecretChatStatus.Accepted } };
                SaveChat(writer, PromoteInitial(writer, accepted));
                foreach (var message in unattached)
                    SaveMessage(writer, message with { ChatId = chat.Info.Id });
                if (reply is not null)
                {
                    SaveMessage(writer, reply);
                    writer.Set(SecretTable.Replay, replayKey, new Replay(replayKey, reply.Id));
                }
            }, token);
            return Task.CompletedTask;
        }, cancellationToken, peer);
    }

    internal Task DeleteFailedMessageAsync(string chatId, string messageId, CancellationToken cancellationToken) =>
        RunAsync(token =>
        {
            var message = Read<SecretChatMessage>(SecretTable.History, messageId);
            if (message is { Outgoing: true, ServerId: null } && message.ChatId == chatId)
            {
                var snapshot = _store.ReadSecretSnapshot(_scope);
                Commit(snapshot, snapshot, writer => writer.Set<SecretChatMessage>(SecretTable.History, messageId, null), token);
            }
            else if (Read<Chat>(SecretTable.Chats, chatId) is { Initial: { ServerId: null } initial } chat &&
                initial.Id == messageId)
            {
                var snapshot = _store.ReadSecretSnapshot(_scope);
                Commit(snapshot, snapshot, writer => SaveChat(writer,
                    chat with { Initial = null, PendingEnvelope = null, ExpiresAt = default }), token);
            }
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<SecretChatMessage> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken = default) =>
        SendMessageAsync(chatId, text, "out:" + Guid.NewGuid().ToString("N"), cancellationToken);

    internal Task<SecretChatMessage> SendMessageAsync(string chatId, string text, string localId, CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            var keys = RequireKeys();
            var snapshot = _store.ReadSecretSnapshot(_scope);
            var chat = Read<Chat>(SecretTable.Chats, chatId);
            if (chat?.Info.Status != SecretChatStatus.Accepted)
                throw new InvalidOperationException("The secret chat is unavailable for sending.");
            var encrypted = Encrypt(snapshot.State, snapshot.Generation, chat.Info.PeerDeviceId, text);
            var history = new SecretChatMessage(localId, chatId, _scope.UserId,
                keys.DeviceId, _clock.GetUtcNow(), text, Outgoing: true);
            snapshot = Commit(snapshot, Updated(snapshot, encrypted), writer => SaveMessage(writer, history), token);
            // У SendSecretMessage нет ключа идемпотентности. После потери ответа статус остаётся неизвестным.
            var sent = await RpcAsync(ct => _gateway.SendSecretMessageAsync(new()
            {
                RecipientUserId = chat.Info.PeerUserId, RecipientDeviceId = chat.Info.PeerDeviceId,
                Envelope = ByteString.CopyFrom(encrypted.Output.Ciphertext)
            }, ct), token);
            if (string.IsNullOrWhiteSpace(sent.MessageId))
                throw new InvalidOperationException("The server returned an invalid secret message ID.");
            history = history with { ServerId = sent.MessageId };
            Commit(snapshot, snapshot, writer => SaveMessage(writer, history), token);
            return history;
        }, cancellationToken);

    public Task<SecretMessageDelivery> ReceiveMessageAsync(SecretEnvelope envelope, CancellationToken cancellationToken = default)
        => ReceiveMessageAsync(envelope, cancellationToken, keepStreamOnAckFailure: false);

    internal Task<SecretMessageDelivery> ReceiveMessageAsync(SecretEnvelope envelope, CancellationToken cancellationToken,
        bool keepStreamOnAckFailure)
    {
        var peer = new Peer(envelope.SenderUserId, envelope.SenderDeviceId);
        return RunAsync(ReceiveAsync, cancellationToken, peer);

        async Task<SecretMessageDelivery> ReceiveAsync(CancellationToken token)
        {
            var replayKey = MessagePrefix(peer) + envelope.MessageId;
            var previous = Read<Replay>(SecretTable.FailedReplay, replayKey);
            var now = _clock.GetUtcNow();
            if (previous is { FailedDecryptions: > 0, FirstFailureAt: { } since } &&
                now - since < FailedReplayRetention && (previous.TerminalFailure || now - since >= DecryptionRetryWindow))
                return new SecretMessageDelivery(null, Duplicate: false, Ignored: true);
            return await ProcessAsync(replayKey, previous, token);
        }

        async Task<SecretMessageDelivery> ProcessAsync(string replayKey, Replay? previous, CancellationToken token)
        {
            var keys = RequireKeys();
            DateTimeOffset? sentAt;
            try
            {
                if (peer.UserId <= 0 || string.IsNullOrWhiteSpace(peer.DeviceId) ||
                    string.IsNullOrWhiteSpace(envelope.MessageId) || envelope.Envelope.IsEmpty ||
                    envelope.RecipientDeviceId != keys.DeviceId)
                    throw new ArgumentException("The secret message envelope is invalid for this device.", nameof(envelope));
                sentAt = envelope.SentAt?.ToDateTimeOffset();
            }
            catch (Exception error) when (keepStreamOnAckFailure && error is ArgumentException or InvalidOperationException)
            {
                RecordReplayFailure(replayKey, previous, terminal: true, token);
                return new SecretMessageDelivery(null, Duplicate: false, Ignored: true);
            }
            var snapshot = _store.ReadSecretSnapshot(_scope);
            if (Read<Replay>(SecretTable.Replay, replayKey) is { FailedDecryptions: 0 } delivered)
            {
                await AckAsync(snapshot, delivered with { PendingAck = envelope.MessageId }, token, keepStreamOnAckFailure);
                return new SecretMessageDelivery(null, Duplicate: true);
            }

            string initialKey = EnvelopeKey(peer.UserId, peer.DeviceId, envelope.Envelope.Span);
            var matchingInvite = Read<Replay>(SecretTable.Replay, initialKey);
            if (matchingInvite is not null)
            {
                var alias = new Replay(replayKey, matchingInvite.HistoryId, envelope.MessageId);
                snapshot = Commit(snapshot, snapshot, writer =>
                {
                    writer.Set(SecretTable.Replay, replayKey, alias);
                    if (previous is not null)
                        writer.Set<Replay>(SecretTable.FailedReplay, replayKey, null);
                }, token);
                await AckAsync(snapshot, alias, token, keepStreamOnAckFailure);
                return new SecretMessageDelivery(null, Duplicate: true);
            }

            var chat = Find(peer);
            if (chat?.Info.Status == SecretChatStatus.KeyChanged)
                return new SecretMessageDelivery(null, Duplicate: false, Ignored: true);
            SignalResult<byte[]> decrypted;
            SignalMessageKind kind;
            try
            {
                decrypted = Decrypt(snapshot, peer, envelope.Envelope, out kind);
            }
            catch (SignalProtocolException error) when (error.Error is SignalError.DuplicateMessage or
                SignalError.SessionNotFound or SignalError.Protocol or SignalError.InvalidInput)
            {
                // Конверт может прийти раньше сообщения, устанавливающего сессию.
                // Ack отправляется только после сохранения сообщения.
                var terminal = error.Error == SignalError.DuplicateMessage ||
                    (error.Error == SignalError.InvalidInput && previous is { FailedDecryptions: >= 2, FirstFailureAt: { } since } &&
                        _clock.GetUtcNow() - since < FailedReplayRetention);
                RecordReplayFailure(replayKey, previous, terminal, token);
                return new SecretMessageDelivery(null, Duplicate: false, Ignored: true);
            }

            var message = new SecretChatMessage(replayKey, chat?.Info.Id, peer.UserId, peer.DeviceId,
                sentAt ?? _clock.GetUtcNow(), DecodeText(decrypted.Output),
                ServerId: envelope.MessageId);
            var replay = new Replay(replayKey, message.Id, envelope.MessageId);
            var retryableFailures = kind == SignalMessageKind.PreKey ? RetryableFailures(peer, replayKey) : [];
            snapshot = Commit(snapshot, Updated(snapshot, decrypted), writer =>
            {
                SaveMessage(writer, message);
                writer.Set(SecretTable.Replay, replayKey, replay);
                if (previous is not null)
                    writer.Set<Replay>(SecretTable.FailedReplay, replayKey, null);
                ClearFailures(writer, retryableFailures);
                if (kind == SignalMessageKind.PreKey)
                    writer.Set(SecretTable.Replay, initialKey, new Replay(initialKey, message.Id));
            }, token);
            if (message.ChatId is { } chatId)
                IncomingMessageStored?.Invoke(chatId);
            await AckAsync(snapshot, replay, token, keepStreamOnAckFailure);
            return new SecretMessageDelivery(message, Duplicate: false) { ConsumedPreKey = kind == SignalMessageKind.PreKey };
        }
    }

    internal static bool CanRetryStreamOperation(Exception error) =>
        error is not OperationCanceledException &&
        error is not Grpc.Core.RpcException { StatusCode: Grpc.Core.StatusCode.Unimplemented or
            Grpc.Core.StatusCode.Unauthenticated or Grpc.Core.StatusCode.Cancelled };

    private void RecordReplayFailure(string key, Replay? previous, bool terminal, CancellationToken token)
    {
        var now = _clock.GetUtcNow();
        var since = previous?.FirstFailureAt;
        if (since is null || now - since.Value >= FailedReplayRetention)
            previous = null;
        var replay = new Replay(key, "", FailedDecryptions: Math.Min((previous?.FailedDecryptions ?? 0) + 1, 3),
            FirstFailureAt: previous?.FirstFailureAt ?? now, TerminalFailure: terminal);
        PruneFailedReplays(token, replay);
    }

    private void PruneFailedReplays(CancellationToken token, Replay? added = null)
    {
        var oldest = _clock.GetUtcNow() - FailedReplayRetention;
        var failures = _store.ReadSecrets<Replay>(_scope, SecretTable.FailedReplay)
            .Where(replay => replay.Key != added?.Key)
            .OrderByDescending(replay => replay.FirstFailureAt).ToArray();
        var expired = failures.Where((replay, index) => index >= MaxFailedReplayEntries - (added is null ? 0 : 1) ||
            replay.FirstFailureAt is null || replay.FirstFailureAt <= oldest).ToArray();
        if (expired.Length == 0 && added is null)
            return;
        var snapshot = _store.ReadSecretSnapshot(_scope);
        Commit(snapshot, snapshot, writer =>
        {
            ClearFailures(writer, expired);
            if (added is not null)
                writer.Set(SecretTable.FailedReplay, added.Key, added);
        }, token);
    }

    private Replay[] RetryableFailures(Peer peer, string? except = null)
    {
        var prefix = MessagePrefix(peer);
        return _store.ReadSecrets<Replay>(_scope, SecretTable.FailedReplay)
            .Where(replay => !replay.TerminalFailure && replay.Key != except &&
                replay.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }

    private static void ClearFailures(SecretWriter writer, IEnumerable<Replay> failures)
    {
        foreach (var failure in failures)
            writer.Set<Replay>(SecretTable.FailedReplay, failure.Key, null);
    }

    public Task RetryPendingOperationsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            PruneFailedReplays(token);
            var snapshot = _store.ReadSecretSnapshot(_scope);
            foreach (var chat in Chats().Where(chat => chat.Info.Status != SecretChatStatus.KeyChanged))
            {
                var current = chat;
                if (chat.PendingEnvelope is not null && chat.ExpiresAt > _clock.GetUtcNow())
                {
                    current = await SendPendingInviteAsync(chat, snapshot, token);
                    snapshot = _store.ReadSecretSnapshot(_scope);
                }
                snapshot = await AcceptPendingInvitesAsync(current, snapshot, token);
            }
            foreach (var replay in _store.ReadSecrets<Replay>(_scope, SecretTable.Replay))
            {
                if (replay.PendingAck is not null)
                    snapshot = await AckAsync(snapshot, replay, token);
                else if (replay.PendingRejection)
                    snapshot = await RejectPendingInviteAsync(snapshot, replay, token);
            }
        }, cancellationToken);

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default) =>
        RunAsync(token =>
        {
            var snapshot = _store.ReadSecretSnapshot(_scope);
            if (snapshot.Revision.Length == 0)
                return Task.CompletedTask;
            var messages = History();
            var chats = Chats();
            Commit(snapshot, snapshot, writer =>
            {
                foreach (var message in messages)
                    writer.Set<SecretChatMessage>(SecretTable.History, message.Id, null);
                foreach (var chat in chats.Where(chat => chat.Initial is not null))
                    SaveChat(writer, chat with { Initial = null });
            }, token);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task ForgetChatAsync(string chatId, CancellationToken cancellationToken = default) =>
        RunAsync(token =>
        {
            var snapshot = _store.ReadSecretSnapshot(_scope);
            var chat = Read<Chat>(SecretTable.Chats, chatId);
            if (chat is null)
                return Task.CompletedTask;
            var deleted = SignalProtocol.DeleteSession(snapshot.State, snapshot.Generation, chat.Info.PeerDeviceId);
            var messages = History();
            Commit(snapshot, Updated(snapshot, deleted), writer =>
            {
                writer.Set<Chat>(SecretTable.Chats, chatId, null);
                foreach (var message in messages.Where(message => message.ChatId == chatId))
                    writer.Set<SecretChatMessage>(SecretTable.History, message.Id, null);
            }, token);
            return Task.CompletedTask;
        }, cancellationToken);

    public async Task ForgetAllAsync()
    {
        CheckCurrent(CancellationToken.None);
        await _store.ForgetSecretChatsAsync(_scope, _epoch);
    }

    private async Task<Chat> SendPendingInviteAsync(Chat chat, SecretSnapshot snapshot, CancellationToken token)
    {
        if (chat.PendingEnvelope is null)
            return chat;
        var sent = await RpcAsync(ct => _gateway.SendSecretChatInviteAsync(new()
        {
            RecipientUserId = chat.Info.PeerUserId, RecipientDeviceId = chat.Info.PeerDeviceId,
            InitialEnvelope = ByteString.CopyFrom(chat.PendingEnvelope)
        }, ct), token);
        if (string.IsNullOrWhiteSpace(sent.InviteId))
            throw new InvalidOperationException("The server returned an invalid secret invitation ID.");
        chat = chat with
        {
            OutgoingInvites = chat.OutgoingInvites.Append(sent.InviteId).Distinct().ToArray(),
            ExpiresAt = sent.InviteExpiresAt?.ToDateTimeOffset() ?? chat.ExpiresAt,
            PendingEnvelope = null,
            Initial = chat.Initial is { } initial ? initial with { ServerId = sent.InviteId } : null
        };
        Commit(snapshot, snapshot, writer => SaveChat(writer, chat), token);
        return chat;
    }

    private async Task<SecretSnapshot> AcceptPendingInvitesAsync(Chat chat, SecretSnapshot snapshot, CancellationToken token)
    {
        foreach (var inviteId in chat.PendingAccepts)
        {
            try
            {
                await RpcAsync(ct => _gateway.AcceptSecretChatInviteAsync(new() { InviteId = inviteId }, ct), token);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.SecretInviteNotFoundException)
            {
                // Буфер мог уже принять запрос до потери ответа либо удалить просроченный инвайт.
                CheckCurrent(token);
            }
            chat = chat with { PendingAccepts = chat.PendingAccepts.Where(id => id != inviteId).ToArray() };
            snapshot = Commit(snapshot, snapshot, writer => SaveChat(writer, chat), token);
        }
        return snapshot;
    }

    private async Task<SecretSnapshot> RejectPendingInviteAsync(SecretSnapshot snapshot, Replay decision, CancellationToken token)
    {
        try
        {
            await RpcAsync(ct => _gateway.RejectSecretChatInviteAsync(new() { InviteId = decision.HistoryId }, ct), token);
        }
        catch (BarkFluff.Shared.Exceptions.Messages.SecretInviteNotFoundException)
        {
            CheckCurrent(token);
        }
        return Commit(snapshot, snapshot, writer => writer.Set(SecretTable.Replay, decision.Key,
            decision with { PendingRejection = false }), token);
    }

    private async Task<SecretSnapshot> AckAsync(SecretSnapshot snapshot, Replay replay, CancellationToken token,
        bool keepStreamOnAckFailure = false)
    {
        try
        {
            await RpcAsync(ct => _gateway.AckSecretMessageAsync(new() { MessageId = replay.PendingAck! }, ct), token);
        }
        catch (Exception error) when (keepStreamOnAckFailure && CanRetryStreamOperation(error))
        {
            // Ack повторится при следующей доставке или синхронизации.
            return snapshot;
        }
        return Commit(snapshot, snapshot, writer => writer.Set(SecretTable.Replay, replay.Key,
            replay with { PendingAck = null }), token);
    }

    private static SignalPreKeyBundle DecodeBundle(PrekeyBundle? bundle, Peer peer)
    {
        if (bundle is null || bundle.DeviceId != peer.DeviceId || bundle.KyberPrekey is null ||
            bundle.SignedPrekey is null || (bundle.HasOneTimePrekey && bundle.OneTimePrekey is null))
            throw InvalidPrekeys("The peer device did not provide a valid PQXDH bundle.");
        return new SignalPreKeyBundle(bundle.RegistrationId, bundle.IdentityPubkey.ToByteArray(),
            bundle.HasOneTimePrekey ? new(bundle.OneTimePrekey.PrekeyId, bundle.OneTimePrekey.PublicKey.ToByteArray()) : null,
            new(bundle.SignedPrekey.PrekeyId, bundle.SignedPrekey.PublicKey.ToByteArray(), bundle.SignedPrekey.Signature.ToByteArray()),
            new(bundle.KyberPrekey.PrekeyId, bundle.KyberPrekey.PublicKey.ToByteArray(), bundle.KyberPrekey.Signature.ToByteArray()));
    }

    private static SignalResult<SignalCiphertext> Encrypt(byte[] state, ulong generation, string peer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try { return SignalProtocol.Encrypt(state, generation, peer, bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static SignalResult<byte[]> Decrypt(SecretSnapshot snapshot, Peer peer, ByteString envelope, out SignalMessageKind kind)
    {
        var bytes = envelope.ToByteArray();
        try
        {
            kind = SignalMessageKind.Signal;
            return SignalProtocol.Decrypt(snapshot.State, snapshot.Generation, peer.DeviceId, kind, bytes);
        }
        catch (SignalProtocolException error) when (error.Error is SignalError.Protocol
            or SignalError.InvalidInput or SignalError.SessionNotFound)
        {
            // Wire-конверт не содержит отдельного поля с типом сообщения.
            kind = SignalMessageKind.PreKey;
            try { return SignalProtocol.Decrypt(snapshot.State, snapshot.Generation, peer.DeviceId, kind, bytes); }
            catch (SignalProtocolException fallback) when (error.Error == SignalError.SessionNotFound &&
                fallback.Error is SignalError.Protocol or SignalError.InvalidInput)
            {
                throw new SignalProtocolException(SignalError.SessionNotFound);
            }
        }
    }

    private static string DecodeText(byte[] plaintext)
    {
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static void RequireTrusted(Chat? chat)
    {
        if (chat?.Info.Status == SecretChatStatus.KeyChanged)
            throw new SignalProtocolException(SignalError.UntrustedIdentity);
    }

    private PrekeyState RequireKeys() => Read<PrekeyState>(SecretTable.Counters, PrekeyStateId)
        ?? throw InvalidPrekeys("Synchronize this device's PQXDH keys first.");

    private T? Read<T>(SecretTable table, string id) where T : class => _store.ReadSecret<T>(_scope, table, id);
    private IReadOnlyList<Chat> Chats() => _store.ReadSecrets<Chat>(_scope, SecretTable.Chats);
    private IReadOnlyList<SecretChatMessage> History() => _store.ReadSecrets<SecretChatMessage>(_scope, SecretTable.History);
    private SecretChatMessage[] UnattachedHistory(Peer peer) => History().Where(message => message.ChatId is null &&
        message.SenderUserId == peer.UserId && message.SenderDeviceId == peer.DeviceId).ToArray();
    private Chat? Find(Peer peer) => Chats().SingleOrDefault(chat =>
        chat.Info.PeerUserId == peer.UserId && chat.Info.PeerDeviceId == peer.DeviceId);
    private static SecretChatInfo NewChat(Peer peer, SecretChatStatus status) =>
        new(Guid.NewGuid().ToString("N"), peer.UserId, peer.DeviceId, status);
    private static void SaveChat(SecretWriter writer, Chat chat) => writer.Set(SecretTable.Chats, chat.Info.Id, chat);
    private static void SaveMessage(SecretWriter writer, SecretChatMessage message) =>
        writer.Set(SecretTable.History, message.Id, message);

    private static Chat PromoteInitial(SecretWriter writer, Chat chat)
    {
        if (chat.Initial is null)
            return chat;
        SaveMessage(writer, chat.Initial);
        return chat with { Initial = null };
    }

    private static string PeerKey(long user, string device) => $"{user}:{device.Length}:{device}";
    private static string MessagePrefix(Peer peer) => $"message:{PeerKey(peer.UserId, peer.DeviceId)}:";
    private static string EnvelopeKey(long user, string device, ReadOnlySpan<byte> envelope) =>
        $"invite:{PeerKey(user, device)}:{Convert.ToHexString(SHA256.HashData(envelope))}";

    private static SecretSnapshot Updated<T>(SecretSnapshot snapshot, SignalResult<T> result) =>
        snapshot with { State = result.State, Generation = result.Generation };

    private SecretSnapshot Commit(SecretSnapshot expected, SecretSnapshot next, Action<SecretWriter> write, CancellationToken token)
    {
        CheckCurrent(token);
        return _store.CommitSecret(_scope, expected, next.State, next.Generation, write);
    }

    private void CheckCurrent(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_scopeLock.Epoch != _epoch || !_isCurrent())
            throw new OperationCanceledException("The secret chat session has ended.", token);
    }

    private async Task<T> RpcAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken token) where T : class
    {
        CheckCurrent(token);
        var response = await call(token);
        CheckCurrent(token);
        return response ?? throw new InvalidOperationException("The secret chat RPC returned no result.");
    }

    private Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token, Peer? peer = null) =>
        RunAsync(async ct => { await action(ct); return true; }, token, peer);

    private Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token, Peer? peer = null) =>
        Task.Run(async () =>
        {
            SecretChatInfo? changed = null;
            await _scopeLock.Gate.WaitAsync(token);
            try
            {
                CheckCurrent(token);
                return await action(token);
            }
            catch (SignalProtocolException error) when (peer is not null && error.Error == SignalError.UntrustedIdentity)
            {
                var chat = Find(peer);
                if (chat?.Info.Status != SecretChatStatus.KeyChanged)
                {
                    var snapshot = _store.ReadSecretSnapshot(_scope);
                    var info = (chat?.Info ?? NewChat(peer, SecretChatStatus.KeyChanged)) with
                    { Status = SecretChatStatus.KeyChanged };
                    Commit(snapshot, snapshot, writer => SaveChat(writer, (chat ?? new Chat(info, [], [])) with { Info = info }), token);
                    changed = info;
                }
                throw;
            }
            finally
            {
                _scopeLock.Gate.Release();
                if (changed is not null && _scopeLock.Epoch == _epoch && _isCurrent())
                    PeerIdentityChanged?.Invoke(changed);
            }
        }, token);
}
