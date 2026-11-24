using BarkFluff.Proto.Users;
using Google.Protobuf;
using static BarkFluff.Client.Core.Infrastructure.Storage.SqliteApplicationDataStore;

namespace BarkFluff.Client.Core.Services.SecretChats;

public sealed partial class SecretChatService
{
    private const int PoolSize = 100;
    private const int ReplenishThreshold = 10;
    private static readonly TimeSpan RotationInterval = TimeSpan.FromDays(7);
    private const string PrekeyStateId = "prekeys";

    private sealed record PrekeyState(string DeviceId, SignalDevice Identity,
        uint NextEc = 1, uint NextSigned = 1, uint NextKyber = 1,
        uint Signed = 0, uint LastResort = 0,
        byte[]? Registration = null, byte[]? Rotation = null, byte[]? Replenishment = null);

    public Task SynchronizePrekeysAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            var device = (await RpcAsync(ct => _gateway.GetCurrentDeviceAsync(new(), ct), token)).Device;
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceId) || device.UserId != _scope.UserId)
                throw new InvalidOperationException("The server returned an invalid current device.");

            var snapshot = _store.ReadSecretSnapshot(_scope);
            var keys = Read<PrekeyState>(SecretTable.Counters, PrekeyStateId);
            var status = await StatusAsync(token);
            if (keys is null)
            {
                if (status.HasBundle || snapshot.Revision.Length != 0)
                    throw InvalidPrekeys("Local device keys are unavailable. Re-register this device.");
                var identity = SignalProtocol.CreateDevice();
                snapshot = snapshot with { State = identity.State, Generation = identity.Generation };
                keys = new PrekeyState(device.DeviceId, identity.Output);
            }
            if (device.DeviceId != keys.DeviceId)
                throw InvalidPrekeys("The current device has changed.");
            RequireIdentity(keys, status);

            if (keys.Rotation is { } pendingRotation &&
                Matches(status, RotateSignedPrekeyRequest.Parser.ParseFrom(pendingRotation).LastResortKyberPrekey))
            {
                keys = FinalizeRotation(keys, RotateSignedPrekeyRequest.Parser.ParseFrom(pendingRotation));
                snapshot = SaveKeys(snapshot, keys, token);
            }

            if (keys.Registration is not null || !status.HasBundle)
            {
                if (keys.Registration is null)
                {
                    var generated = new KeyGeneration(snapshot, keys);
                    var signed = generated.Signed();
                    var lastResort = generated.Kyber(lastResort: true);
                    var request = new RegisterPrekeyBundleRequest
                    {
                        IdentityPubkey = ByteString.CopyFrom(keys.Identity.IdentityKey),
                        RegistrationId = keys.Identity.RegistrationId,
                        SignedPrekey = signed,
                        LastResortKyberPrekey = lastResort
                    };
                    request.OneTimePrekeys.Add(Enumerable.Range(0, PoolSize).Select(_ => generated.Ec()));
                    request.OneTimeKyberPrekeys.Add(Enumerable.Range(0, PoolSize).Select(_ => generated.Kyber()));
                    keys = generated.Keys with { Signed = signed.PrekeyId, LastResort = lastResort.PrekeyId,
                        Registration = request.ToByteArray(), Rotation = null, Replenishment = null };
                    snapshot = SaveKeys(generated.Snapshot, keys, token, snapshot);
                }

                if (!LocalLastResortMatches(snapshot, keys, status))
                {
                    if (status.SupportsPqxdh)
                        throw InvalidPrekeys("The server's PQXDH last-resort key differs from the local key.");
                    var request = RegisterPrekeyBundleRequest.Parser.ParseFrom(keys.Registration);
                    await RpcAsync(ct => _gateway.RegisterPrekeyBundleAsync(request, ct), token);
                    status = await StatusAsync(token);
                    RequireIdentity(keys, status);
                }
                RequireLastResort(snapshot, keys, status);
                keys = keys with { Registration = null };
                snapshot = SaveKeys(snapshot, keys, token);
            }
            RequireLastResort(snapshot, keys, status);

            var signedKey = SignalProtocol.GetSignedPreKey(snapshot.State, snapshot.Generation, keys.Signed).Output;
            var kyberKey = SignalProtocol.GetKyberPreKey(snapshot.State, snapshot.Generation, keys.LastResort).Output;
            if (keys.Rotation is not null || IsDue(signedKey) || IsDue(kyberKey))
            {
                if (keys.Rotation is null)
                {
                    var generated = new KeyGeneration(snapshot, keys);
                    var request = new RotateSignedPrekeyRequest
                    {
                        SignedPrekey = generated.Signed(),
                        LastResortKyberPrekey = generated.Kyber(lastResort: true)
                    };
                    keys = generated.Keys with { Rotation = request.ToByteArray() };
                    snapshot = SaveKeys(generated.Snapshot, keys, token, snapshot);
                }
                var rotation = RotateSignedPrekeyRequest.Parser.ParseFrom(keys.Rotation);
                await RpcAsync(ct => _gateway.RotateSignedPrekeyAsync(rotation, ct), token);
                status = await StatusAsync(token);
                RequireIdentity(keys, status);
                keys = FinalizeRotation(keys, rotation);
                RequireLastResort(snapshot, keys, status);
                // Старые signed и last-resort ключи нужны для отложенных конвертов, как на Android.
                // Одноразовые EC и Kyber ключи удаляет libsignal при расшифровке.
                snapshot = SaveKeys(snapshot, keys, token);
            }

            var ec = ReplenishmentSize(status.RemainingOneTimePrekeys);
            var kyber = ReplenishmentSize(status.RemainingOneTimeKyberPrekeys);
            if (keys.Replenishment is null && ec == 0 && kyber == 0)
                return;
            if (keys.Replenishment is null)
            {
                var generated = new KeyGeneration(snapshot, keys);
                var request = new ReplenishOneTimePrekeysRequest();
                request.Prekeys.Add(Enumerable.Range(0, ec).Select(_ => generated.Ec()));
                request.KyberPrekeys.Add(Enumerable.Range(0, kyber).Select(_ => generated.Kyber()));
                keys = generated.Keys with { Replenishment = request.ToByteArray() };
                snapshot = SaveKeys(generated.Snapshot, keys, token, snapshot);
            }
            var replenishment = ReplenishOneTimePrekeysRequest.Parser.ParseFrom(keys.Replenishment);
            await RpcAsync(ct => _gateway.ReplenishOneTimePrekeysAsync(replenishment, ct), token);
            status = await StatusAsync(token);
            RequireIdentity(keys, status);
            RequireLastResort(snapshot, keys, status);
            SaveKeys(snapshot, keys with { Replenishment = null }, token);
        }, cancellationToken);

    private Task<GetOwnPrekeyStatusResponse> StatusAsync(CancellationToken token) =>
        RpcAsync(ct => _gateway.GetOwnPrekeyStatusAsync(new(), ct), token);

    private bool IsDue(SignalSignedPreKey key) =>
        _clock.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(checked((long)key.Timestamp)) >= RotationInterval;

    private static int ReplenishmentSize(int remaining) =>
        remaining < ReplenishThreshold ? PoolSize - Math.Clamp(remaining, 0, PoolSize) : 0;

    private static PrekeyState FinalizeRotation(PrekeyState keys, RotateSignedPrekeyRequest request) =>
        keys with { Signed = request.SignedPrekey.PrekeyId, LastResort = request.LastResortKyberPrekey.PrekeyId, Rotation = null };

    private SecretSnapshot SaveKeys(SecretSnapshot snapshot, PrekeyState keys, CancellationToken token,
        SecretSnapshot? expected = null) =>
        Commit(expected ?? snapshot, snapshot, writer => writer.Set(SecretTable.Counters, PrekeyStateId, keys), token);

    private static void RequireIdentity(PrekeyState keys, GetOwnPrekeyStatusResponse status)
    {
        if (status.HasBundle && (status.RegistrationId != keys.Identity.RegistrationId ||
            !status.IdentityPubkey.Span.SequenceEqual(keys.Identity.IdentityKey)))
            throw InvalidPrekeys("The server's device identity differs from the local identity.");
    }

    private static bool Matches(GetOwnPrekeyStatusResponse status, SignedKyberPreKey key) =>
        status.HasBundle && status.SupportsPqxdh && key.Equals(status.LastResortKyberPrekey);

    private static bool LocalLastResortMatches(SecretSnapshot snapshot, PrekeyState keys, GetOwnPrekeyStatusResponse status) =>
        status.LastResortKyberPrekey?.PrekeyId == keys.LastResort &&
        Matches(status, ToKyber(SignalProtocol.GetKyberPreKey(snapshot.State, snapshot.Generation, keys.LastResort).Output));

    private static void RequireLastResort(SecretSnapshot snapshot, PrekeyState keys, GetOwnPrekeyStatusResponse status)
    {
        if (!LocalLastResortMatches(snapshot, keys, status))
            throw InvalidPrekeys("The server did not confirm the local PQXDH last-resort key.");
    }

    private static InvalidOperationException InvalidPrekeys(string message) => new(message);

    private static SignedKyberPreKey ToKyber(SignalSignedPreKey key) => new()
    {
        PrekeyId = key.Id, PublicKey = ByteString.CopyFrom(key.PublicKey), Signature = ByteString.CopyFrom(key.Signature)
    };

    private sealed class KeyGeneration(SecretSnapshot snapshot, PrekeyState keys)
    {
        internal SecretSnapshot Snapshot { get; private set; } = snapshot;
        internal PrekeyState Keys { get; private set; } = keys;

        private T Apply<T>(SignalResult<T> result)
        {
            Snapshot = Updated(Snapshot, result);
            return result.Output;
        }

        internal OneTimePreKey Ec()
        {
            var id = Keys.NextEc;
            Keys = Keys with { NextEc = checked(id + 1) };
            var key = Apply(SignalProtocol.GeneratePreKey(Snapshot.State, Snapshot.Generation, id));
            return new OneTimePreKey { PrekeyId = key.Id, PublicKey = ByteString.CopyFrom(key.PublicKey) };
        }

        internal SignedPreKey Signed()
        {
            var id = Keys.NextSigned;
            Keys = Keys with { NextSigned = checked(id + 1) };
            var key = Apply(SignalProtocol.GenerateSignedPreKey(Snapshot.State, Snapshot.Generation, id));
            return new SignedPreKey { PrekeyId = key.Id, PublicKey = ByteString.CopyFrom(key.PublicKey),
                Signature = ByteString.CopyFrom(key.Signature) };
        }

        internal SignedKyberPreKey Kyber(bool lastResort = false)
        {
            var id = Keys.NextKyber;
            Keys = Keys with { NextKyber = checked(id + 1) };
            return ToKyber(Apply(SignalProtocol.GenerateKyberPreKey(Snapshot.State, Snapshot.Generation, id, lastResort)));
        }
    }
}
