using BarkFluff.Client.Core.Services.SecretChats;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;
using BarkFluff.Proto.Users;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Client.WinUI.Tests;

internal sealed class SecretChatTestServer
{
    private readonly Dictionary<string, Endpoint> _devices = [];
    private int _nextId;

    internal Endpoint AddDevice(long userId, string id)
    {
        var device = new Endpoint(this, userId, id);
        _devices.Add(id, device);
        return device;
    }

    private string NextId() => Interlocked.Increment(ref _nextId).ToString();

    internal sealed class Endpoint(SecretChatTestServer server, long userId, string id) : ISecretChatGateway
    {
        internal long UserId => userId;
        internal string Id => id;
        internal RegisterPrekeyBundleRequest? Registration;
        internal readonly List<OneTimePreKey> Ec = [];
        internal readonly List<SignedKyberPreKey> Kyber = [];
        internal readonly List<ReplenishOneTimePrekeysRequest> Replenishments = [];
        internal readonly List<RotateSignedPrekeyRequest> Rotations = [];
        internal readonly List<SecretChatInviteEvent> Invites = [];
        internal readonly List<SecretChatInviteResolutionEvent> Resolutions = [];
        internal readonly List<SecretEnvelope> Messages = [];
        internal readonly List<string> Acks = [];

        internal Action? OnFetch;
        internal Action? OnSendInvite;
        internal Action? OnAccept;
        internal Action? AfterRegister;
        internal Action? AfterRotate;
        internal Action? AfterReplenish;
        internal Action<string>? BeforeAck;
        internal Func<PrekeyBundle, PrekeyBundle>? MapBundle;
        internal Func<GetOwnPrekeyStatusResponse, GetOwnPrekeyStatusResponse>? MapStatus;
        private readonly List<(Endpoint Sender, string InviteId)> _pendingInvites = [];

        public Task<GetCurrentDeviceResponse> GetCurrentDeviceAsync(GetCurrentDeviceRequest request, CancellationToken ct) =>
            Task.FromResult(new GetCurrentDeviceResponse { Device = new() { DeviceId = Id, UserId = UserId } });

        public Task<GetOwnPrekeyStatusResponse> GetOwnPrekeyStatusAsync(GetOwnPrekeyStatusRequest request, CancellationToken ct)
        {
            var status = new GetOwnPrekeyStatusResponse
            {
                HasBundle = Registration is not null, SupportsPqxdh = Registration?.LastResortKyberPrekey is not null,
                IdentityPubkey = Registration?.IdentityPubkey ?? ByteString.Empty, RegistrationId = Registration?.RegistrationId ?? 0,
                LastResortKyberPrekey = Registration?.LastResortKyberPrekey?.Clone(),
                RemainingOneTimePrekeys = Ec.Count, RemainingOneTimeKyberPrekeys = Kyber.Count
            };
            return Task.FromResult(MapStatus?.Invoke(status) ?? status);
        }

        public Task<RegisterPrekeyBundleResponse> RegisterPrekeyBundleAsync(RegisterPrekeyBundleRequest request, CancellationToken ct)
        {
            Registration = request.Clone();
            Ec.AddRange(request.OneTimePrekeys);
            Kyber.AddRange(request.OneTimeKyberPrekeys);
            AfterRegister?.Invoke();
            return Task.FromResult(new RegisterPrekeyBundleResponse());
        }

        public Task<RotateSignedPrekeyResponse> RotateSignedPrekeyAsync(RotateSignedPrekeyRequest request, CancellationToken ct)
        {
            Rotations.Add(request.Clone());
            Registration!.SignedPrekey = request.SignedPrekey;
            Registration.LastResortKyberPrekey = request.LastResortKyberPrekey;
            AfterRotate?.Invoke();
            return Task.FromResult(new RotateSignedPrekeyResponse());
        }

        public Task<ReplenishOneTimePrekeysResponse> ReplenishOneTimePrekeysAsync(
            ReplenishOneTimePrekeysRequest request, CancellationToken ct)
        {
            Replenishments.Add(request.Clone());
            Ec.AddRange(request.Prekeys.Where(key => Ec.All(prior => prior.PrekeyId != key.PrekeyId)));
            Kyber.AddRange(request.KyberPrekeys.Where(key => Kyber.All(prior => prior.PrekeyId != key.PrekeyId)));
            AfterReplenish?.Invoke();
            return Task.FromResult(new ReplenishOneTimePrekeysResponse
            { TotalOneTimePrekeys = Ec.Count, TotalOneTimeKyberPrekeys = Kyber.Count });
        }

        public Task<ListPeerDevicesResponse> ListPeerDevicesAsync(ListPeerDevicesRequest request, CancellationToken ct)
        {
            var response = new ListPeerDevicesResponse();
            response.Devices.Add(server._devices.Values.Where(device => device.UserId == request.UserId)
                .Select(device => new PeerDeviceInfo
                {
                    DeviceId = device.Id, HasBundle = device.Registration is not null,
                    SupportsPqxdh = device.Registration?.LastResortKyberPrekey is not null
                }));
            return Task.FromResult(response);
        }

        public Task<FetchPrekeyBundleResponse> FetchPrekeyBundleAsync(FetchPrekeyBundleRequest request, CancellationToken ct)
        {
            OnFetch?.Invoke();
            Assert.True(request.RequirePqxdh);
            var peer = server._devices[request.DeviceId];
            Assert.Equal(request.UserId, peer.UserId);
            var registration = peer.Registration!;
            var ec = peer.Ec.FirstOrDefault();
            var kyber = peer.Kyber.FirstOrDefault();
            if (ec is not null) peer.Ec.RemoveAt(0);
            if (kyber is not null) peer.Kyber.RemoveAt(0);
            var bundle = new PrekeyBundle
            {
                DeviceId = peer.Id, RegistrationId = registration.RegistrationId, IdentityPubkey = registration.IdentityPubkey,
                SignedPrekey = registration.SignedPrekey, OneTimePrekey = ec, HasOneTimePrekey = ec is not null,
                KyberPrekey = kyber ?? registration.LastResortKyberPrekey, KyberPrekeyIsLastResort = kyber is null
            };
            return Task.FromResult(new FetchPrekeyBundleResponse { Bundle = MapBundle?.Invoke(bundle) ?? bundle });
        }

        public Task<SendSecretChatInviteResponse> SendSecretChatInviteAsync(SendSecretChatInviteRequest request, CancellationToken ct)
        {
            OnSendInvite?.Invoke();
            var peer = server._devices[request.RecipientDeviceId];
            Assert.Equal(request.RecipientUserId, peer.UserId);
            var inviteId = server.NextId();
            var expiry = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(1));
            peer._pendingInvites.Add((this, inviteId));
            peer.Invites.Add(new SecretChatInviteEvent
            {
                InviteId = inviteId, SenderDeviceId = Id, SenderUserId = UserId, InitialEnvelope = request.InitialEnvelope,
                InvitedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow), ExpiresAt = expiry
            });
            return Task.FromResult(new SendSecretChatInviteResponse { InviteId = inviteId, InviteExpiresAt = expiry });
        }

        public Task<AcceptSecretChatInviteResponse> AcceptSecretChatInviteAsync(AcceptSecretChatInviteRequest request, CancellationToken ct)
        {
            OnAccept?.Invoke();
            Resolve(request.InviteId, true, request.ResponseEnvelope);
            return Task.FromResult(new AcceptSecretChatInviteResponse());
        }

        public Task<RejectSecretChatInviteResponse> RejectSecretChatInviteAsync(RejectSecretChatInviteRequest request, CancellationToken ct)
        {
            Resolve(request.InviteId, false, ByteString.Empty);
            return Task.FromResult(new RejectSecretChatInviteResponse());
        }

        private void Resolve(string inviteId, bool accepted, ByteString response)
        {
            var entry = _pendingInvites.SingleOrDefault(invite => invite.InviteId == inviteId);
            if (entry.Sender is null)
                throw new BarkFluff.Shared.Exceptions.Messages.SecretInviteNotFoundException();
            _pendingInvites.Remove(entry);
            entry.Sender.Resolutions.Add(new SecretChatInviteResolutionEvent
            {
                InviteId = inviteId, RecipientUserId = UserId, RecipientDeviceId = Id, Accepted = accepted,
                ResponseEnvelope = response
            });
        }

        public Task<SendSecretMessageResponse> SendSecretMessageAsync(SendSecretMessageRequest request, CancellationToken ct)
        {
            var peer = server._devices[request.RecipientDeviceId];
            Assert.Equal(request.RecipientUserId, peer.UserId);
            var messageId = server.NextId();
            peer.Messages.Add(new SecretEnvelope
            {
                MessageId = messageId, SenderUserId = UserId, SenderDeviceId = Id,
                RecipientDeviceId = peer.Id, Envelope = request.Envelope,
                SentAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
            });
            return Task.FromResult(new SendSecretMessageResponse { MessageId = messageId });
        }

        public Task<AckSecretMessageResponse> AckSecretMessageAsync(AckSecretMessageRequest request, CancellationToken ct)
        {
            BeforeAck?.Invoke(request.MessageId);
            Acks.Add(request.MessageId);
            return Task.FromResult(new AckSecretMessageResponse());
        }
    }
}
