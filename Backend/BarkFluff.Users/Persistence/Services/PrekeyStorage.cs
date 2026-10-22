using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class PrekeyStorage(UsersContext context)
{
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public async Task<DevicePrekeyBundle> RegisterBundleAsync(
        Guid deviceId,
        long userId,
        long registrationId,
        byte[] identityPubkey,
        long signedPrekeyId,
        byte[] signedPrekeyPublic,
        byte[] signedPrekeySignature,
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> oneTimePrekeys,
        KyberPrekeyInput? lastResortKyberPrekey = null,
        IReadOnlyList<KyberPrekeyInput>? oneTimeKyberPrekeys = null,
        CancellationToken cancellationToken = default)
    {
        var kyberPrekeys = oneTimeKyberPrekeys ?? [];
        ValidateRegistration(identityPubkey, signedPrekeyPublic, signedPrekeySignature, oneTimePrekeys, lastResortKyberPrekey, kyberPrekeys);

        return await ExecuteDeviceTransactionAsync(deviceId, userId, requireDevice: true, async () =>
        {
            var existing = await context.DevicePrekeyBundles
                .FirstOrDefaultAsync(bundle => bundle.DeviceId == deviceId, cancellationToken);
            var now = DateTime.UtcNow;
            var replacingRegistration = existing != null &&
                (existing.RegistrationId != registrationId || !existing.IdentityPubkey.SequenceEqual(identityPubkey));

            if (replacingRegistration && HasLastResortKyberPrekey(existing!) && lastResortKyberPrekey == null)
            {
                throw new PrekeyException(
                    StatusCode.FailedPrecondition,
                    "Замена регистрации с опубликованным PQXDH требует нового last-resort Kyber prekey");
            }

            if (existing == null)
            {
                existing = new DevicePrekeyBundle
                {
                    DeviceId = deviceId,
                    CreatedAt = now,
                };
                await context.DevicePrekeyBundles.AddAsync(existing, cancellationToken);
            }
            else if (replacingRegistration)
            {
                await InvalidateOneTimePoolsAsync(deviceId, now, cancellationToken);
                existing.LastResortKyberPrekeyId = null;
                existing.LastResortKyberPrekeyPublic = null;
                existing.LastResortKyberPrekeySignature = null;
            }

            existing.RegistrationId = registrationId;
            existing.IdentityPubkey = identityPubkey;
            existing.SignedPrekeyId = signedPrekeyId;
            existing.SignedPrekeyPublic = signedPrekeyPublic;
            existing.SignedPrekeySignature = signedPrekeySignature;
            existing.SignedPrekeyRotatedAt = now;

            if (lastResortKyberPrekey != null)
            {
                await EnsureKyberIdIsNotInOneTimePoolAsync(deviceId, lastResortKyberPrekey.PrekeyId, cancellationToken);
                existing.LastResortKyberPrekeyId = lastResortKyberPrekey.PrekeyId;
                existing.LastResortKyberPrekeyPublic = lastResortKyberPrekey.PublicKey;
                existing.LastResortKyberPrekeySignature = lastResortKyberPrekey.Signature;
            }

            var uniqueEcPrekeys = CollapseEcPrekeys(oneTimePrekeys);
            var uniqueKyberPrekeys = CollapseKyberPrekeys(kyberPrekeys);
            await AddEcPrekeysIdempotentlyAsync(deviceId, uniqueEcPrekeys, now, cancellationToken);
            await AddKyberPrekeysIdempotentlyAsync(deviceId, uniqueKyberPrekeys, now, cancellationToken);

            return existing;
        }, cancellationToken) ?? throw new InvalidOperationException("Не удалось зарегистрировать bundle устройства");
    }

    public async Task<DevicePrekeyBundle> RotateSignedPrekeyAsync(
        Guid deviceId,
        long userId,
        (long PrekeyId, byte[] PublicKey, byte[] Signature)? signedPrekey,
        KyberPrekeyInput? lastResortKyberPrekey,
        CancellationToken cancellationToken = default)
    {
        if (signedPrekey == null && lastResortKyberPrekey == null)
        {
            throw new PrekeyException(StatusCode.InvalidArgument, "Нужно передать signed EC prekey или last-resort Kyber prekey");
        }

        if (signedPrekey is { } ec)
        {
            PrekeyMaterialValidator.ValidateKey("SignedPrekey.PublicKey", ec.PublicKey);
            PrekeyMaterialValidator.ValidateSignature("SignedPrekey.Signature", ec.Signature);
        }

        if (lastResortKyberPrekey != null)
        {
            ValidateKyberPrekey(lastResortKyberPrekey, "LastResortKyberPrekey");
        }

        return await ExecuteDeviceTransactionAsync(deviceId, userId, requireDevice: true, async () =>
        {
            var bundle = await context.DevicePrekeyBundles
                .FirstOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken);
            if (bundle == null)
            {
                throw new PrekeyException(StatusCode.FailedPrecondition, "Bundle устройства не зарегистрирован");
            }

            if (signedPrekey is { } newEc)
            {
                bundle.SignedPrekeyId = newEc.PrekeyId;
                bundle.SignedPrekeyPublic = newEc.PublicKey;
                bundle.SignedPrekeySignature = newEc.Signature;
                bundle.SignedPrekeyRotatedAt = DateTime.UtcNow;
            }

            if (lastResortKyberPrekey != null)
            {
                await EnsureKyberIdIsNotInOneTimePoolAsync(deviceId, lastResortKyberPrekey.PrekeyId, cancellationToken);
                bundle.LastResortKyberPrekeyId = lastResortKyberPrekey.PrekeyId;
                bundle.LastResortKyberPrekeyPublic = lastResortKyberPrekey.PublicKey;
                bundle.LastResortKyberPrekeySignature = lastResortKyberPrekey.Signature;
            }

            return bundle;
        }, cancellationToken) ?? throw new InvalidOperationException("Не удалось ротировать prekey устройства");
    }

    public async Task<PrekeyPoolCounts> ReplenishOneTimePrekeysAsync(
        Guid deviceId,
        long userId,
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> prekeys,
        IReadOnlyList<KyberPrekeyInput>? kyberPrekeys = null,
        CancellationToken cancellationToken = default)
    {
        var signedKyberPrekeys = kyberPrekeys ?? [];
        ValidateEcPrekeys(prekeys, "Prekeys");
        ValidateKyberPrekeys(signedKyberPrekeys, "KyberPrekeys");

        return await ExecuteDeviceTransactionAsync(deviceId, userId, requireDevice: true, async () =>
        {
            var bundle = await context.DevicePrekeyBundles
                .FirstOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken);
            if (bundle == null)
            {
                throw new PrekeyException(StatusCode.FailedPrecondition, "Bundle устройства не зарегистрирован");
            }

            if (signedKyberPrekeys.Count > 0 && !HasLastResortKyberPrekey(bundle))
            {
                throw new PrekeyException(StatusCode.FailedPrecondition, "Kyber pool требует опубликованный last-resort Kyber prekey");
            }

            var now = DateTime.UtcNow;
            await AddEcPrekeysIdempotentlyAsync(deviceId, CollapseEcPrekeys(prekeys), now, cancellationToken);
            await AddKyberPrekeysIdempotentlyAsync(deviceId, CollapseKyberPrekeys(signedKyberPrekeys), now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            return new PrekeyPoolCounts(
                await context.OneTimePrekeys.CountAsync(prekey => prekey.DeviceId == deviceId && prekey.ConsumedAt == null, cancellationToken),
                await context.OneTimeKyberPrekeys.CountAsync(prekey => prekey.DeviceId == deviceId && prekey.ConsumedAt == null, cancellationToken));
        }, cancellationToken) ?? throw new InvalidOperationException("Не удалось пополнить prekey pool");
    }

    public async Task<FetchedPrekeyBundle?> FetchBundleAsync(
        long peerUserId,
        Guid peerDeviceId,
        bool requirePqxdh = false,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteDeviceTransactionAsync(peerDeviceId, peerUserId, requireDevice: false, async () =>
        {
            var bundle = await context.DevicePrekeyBundles
                .FirstOrDefaultAsync(candidate => candidate.DeviceId == peerDeviceId, cancellationToken);
            if (bundle == null)
            {
                return null;
            }

            if (requirePqxdh && !HasLastResortKyberPrekey(bundle))
            {
                throw new PrekeyException(StatusCode.FailedPrecondition, "Устройство не поддерживает PQXDH");
            }

            var ecPrekey = await ConsumeNextEcPrekeyAsync(peerDeviceId, cancellationToken);
            OneTimeKyberPrekey? kyberPrekey = null;
            var kyberIsLastResort = false;

            if (requirePqxdh)
            {
                kyberPrekey = await ConsumeNextKyberPrekeyAsync(peerDeviceId, cancellationToken);
                if (kyberPrekey == null)
                {
                    kyberIsLastResort = true;
                    kyberPrekey = new OneTimeKyberPrekey
                    {
                        DeviceId = peerDeviceId,
                        PrekeyId = bundle.LastResortKyberPrekeyId!.Value,
                        PublicKey = bundle.LastResortKyberPrekeyPublic!,
                        Signature = bundle.LastResortKyberPrekeySignature!,
                    };
                }
            }

            return new FetchedPrekeyBundle(
                bundle,
                ecPrekey,
                kyberPrekey,
                kyberIsLastResort,
                await context.OneTimePrekeys.CountAsync(prekey => prekey.DeviceId == peerDeviceId && prekey.ConsumedAt == null, cancellationToken),
                await context.OneTimeKyberPrekeys.CountAsync(prekey => prekey.DeviceId == peerDeviceId && prekey.ConsumedAt == null, cancellationToken));
        }, cancellationToken);
    }

    public async Task<OwnPrekeyStatus> GetOwnPrekeyStatusAsync(
        Guid deviceId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteDeviceTransactionAsync(deviceId, userId, requireDevice: true, async () =>
        {
            var bundle = await context.DevicePrekeyBundles
                .FirstOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken);

            return new OwnPrekeyStatus(
                bundle,
                await context.OneTimePrekeys.CountAsync(prekey => prekey.DeviceId == deviceId && prekey.ConsumedAt == null, cancellationToken),
                await context.OneTimeKyberPrekeys.CountAsync(prekey => prekey.DeviceId == deviceId && prekey.ConsumedAt == null, cancellationToken));
        }, cancellationToken) ?? throw new InvalidOperationException("Не удалось получить статус prekey устройства");
    }

    public async Task<List<(UserDevice Device, bool HasBundle, bool SupportsPqxdh)>> ListPeerDevicesAsync(
        long peerUserId,
        CancellationToken cancellationToken = default)
    {
        var devices = await context.UserDevices
            .Where(device => device.UserId == peerUserId)
            .OrderByDescending(device => device.AuthorizedAt)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            return [];
        }

        var deviceIds = devices.Select(device => device.Id).ToList();
        var bundles = await context.DevicePrekeyBundles
            .Where(bundle => deviceIds.Contains(bundle.DeviceId))
            .Select(bundle => new
            {
                bundle.DeviceId,
                HasLastResort = bundle.LastResortKyberPrekeyId != null &&
                    bundle.LastResortKyberPrekeyPublic != null &&
                    bundle.LastResortKyberPrekeySignature != null,
            })
            .ToListAsync(cancellationToken);
        var bundleStatus = bundles.ToDictionary(bundle => bundle.DeviceId, bundle => bundle.HasLastResort);

        return devices
            .Select(device =>
            {
                var supportsPqxdh = bundleStatus.TryGetValue(device.Id, out var supports) && supports;
                return (device, bundleStatus.ContainsKey(device.Id), supportsPqxdh);
            })
            .ToList();
    }

    private async Task<T?> ExecuteDeviceTransactionAsync<T>(
        Guid deviceId,
        long userId,
        bool requireDevice,
        Func<Task<T?>> action,
        CancellationToken cancellationToken)
        where T : class
    {
        if (context.Database.ProviderName != PostgreSqlProvider)
        {
            var ownsDevice = await context.UserDevices
                .AnyAsync(device => device.Id == deviceId && device.UserId == userId, cancellationToken);
            if (!ownsDevice)
            {
                if (requireDevice)
                {
                    throw DeviceOwnershipException();
                }

                return null;
            }

            var nonRelationalResult = await action();
            await context.SaveChangesAsync(cancellationToken);
            return nonRelationalResult;
        }

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var lockedDevice = await context.UserDevices
                .FromSqlInterpolated($"SELECT * FROM \"UserDevices\" WHERE \"Id\" = {deviceId} AND \"UserId\" = {userId} FOR UPDATE")
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (lockedDevice.Count == 0)
            {
                if (requireDevice)
                {
                    throw DeviceOwnershipException();
                }

                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            await context.DevicePrekeyBundles
                .FromSqlInterpolated($"SELECT * FROM \"DevicePrekeyBundles\" WHERE \"DeviceId\" = {deviceId} FOR UPDATE")
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var result = await action();
            await context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            context.ChangeTracker.AcceptAllChanges();
            return result;
        });
    }

    private async Task AddEcPrekeysIdempotentlyAsync(
        Guid deviceId,
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> prekeys,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (prekeys.Count == 0)
        {
            return;
        }

        var ids = prekeys.Select(prekey => prekey.PrekeyId).ToArray();
        var existing = await context.OneTimePrekeys
            .Where(prekey => prekey.DeviceId == deviceId && ids.Contains(prekey.PrekeyId))
            .ToDictionaryAsync(prekey => prekey.PrekeyId, cancellationToken);

        foreach (var prekey in prekeys)
        {
            if (existing.TryGetValue(prekey.PrekeyId, out var current))
            {
                if (!current.PublicKey.SequenceEqual(prekey.PublicKey))
                {
                    throw new PrekeyException(StatusCode.AlreadyExists, $"EC prekey id {prekey.PrekeyId} уже опубликован с другим ключом");
                }

                continue;
            }

            context.OneTimePrekeys.Add(new OneTimePrekey
            {
                DeviceId = deviceId,
                PrekeyId = prekey.PrekeyId,
                PublicKey = prekey.PublicKey,
                CreatedAt = now,
            });
        }
    }

    private async Task AddKyberPrekeysIdempotentlyAsync(
        Guid deviceId,
        IReadOnlyList<KyberPrekeyInput> prekeys,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (prekeys.Count == 0)
        {
            return;
        }

        var ids = prekeys.Select(prekey => prekey.PrekeyId).ToArray();
        var lastResortId = await context.DevicePrekeyBundles
            .Where(bundle => bundle.DeviceId == deviceId)
            .Select(bundle => bundle.LastResortKyberPrekeyId)
            .FirstOrDefaultAsync(cancellationToken);
        if (lastResortId != null && ids.Contains(lastResortId.Value))
        {
            throw new PrekeyException(StatusCode.AlreadyExists, $"Kyber prekey id {lastResortId} уже используется как last-resort key");
        }

        var existing = await context.OneTimeKyberPrekeys
            .Where(prekey => prekey.DeviceId == deviceId && ids.Contains(prekey.PrekeyId))
            .ToDictionaryAsync(prekey => prekey.PrekeyId, cancellationToken);

        foreach (var prekey in prekeys)
        {
            if (existing.TryGetValue(prekey.PrekeyId, out var current))
            {
                if (!current.PublicKey.SequenceEqual(prekey.PublicKey) || !current.Signature.SequenceEqual(prekey.Signature))
                {
                    throw new PrekeyException(StatusCode.AlreadyExists, $"Kyber prekey id {prekey.PrekeyId} уже опубликован с другим payload");
                }

                continue;
            }

            context.OneTimeKyberPrekeys.Add(new OneTimeKyberPrekey
            {
                DeviceId = deviceId,
                PrekeyId = prekey.PrekeyId,
                PublicKey = prekey.PublicKey,
                Signature = prekey.Signature,
                CreatedAt = now,
            });
        }
    }

    private async Task InvalidateOneTimePoolsAsync(Guid deviceId, DateTime now, CancellationToken cancellationToken)
    {
        var ecPrekeys = await context.OneTimePrekeys
            .Where(prekey => prekey.DeviceId == deviceId && prekey.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var prekey in ecPrekeys)
        {
            prekey.ConsumedAt = now;
        }

        var kyberPrekeys = await context.OneTimeKyberPrekeys
            .Where(prekey => prekey.DeviceId == deviceId && prekey.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var prekey in kyberPrekeys)
        {
            prekey.ConsumedAt = now;
        }
    }

    private async Task<OneTimePrekey?> ConsumeNextEcPrekeyAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var prekeys = await context.OneTimePrekeys
            .FromSqlInterpolated($@"
                UPDATE ""OneTimePrekeys""
                SET ""ConsumedAt"" = {DateTime.UtcNow}
                WHERE ""Id"" = (
                    SELECT ""Id"" FROM ""OneTimePrekeys""
                    WHERE ""DeviceId"" = {deviceId} AND ""ConsumedAt"" IS NULL
                    ORDER BY ""Id""
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return prekeys.FirstOrDefault();
    }

    private async Task<OneTimeKyberPrekey?> ConsumeNextKyberPrekeyAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var prekeys = await context.OneTimeKyberPrekeys
            .FromSqlInterpolated($@"
                UPDATE ""OneTimeKyberPrekeys""
                SET ""ConsumedAt"" = {DateTime.UtcNow}
                WHERE ""Id"" = (
                    SELECT ""Id"" FROM ""OneTimeKyberPrekeys""
                    WHERE ""DeviceId"" = {deviceId} AND ""ConsumedAt"" IS NULL
                    ORDER BY ""Id""
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return prekeys.FirstOrDefault();
    }

    private static void ValidateRegistration(
        byte[] identityPubkey,
        byte[] signedPrekeyPublic,
        byte[] signedPrekeySignature,
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> oneTimePrekeys,
        KyberPrekeyInput? lastResortKyberPrekey,
        IReadOnlyList<KyberPrekeyInput> oneTimeKyberPrekeys)
    {
        PrekeyMaterialValidator.ValidateKey("IdentityPubkey", identityPubkey);
        PrekeyMaterialValidator.ValidateKey("SignedPrekey.PublicKey", signedPrekeyPublic);
        PrekeyMaterialValidator.ValidateSignature("SignedPrekey.Signature", signedPrekeySignature);
        ValidateEcPrekeys(oneTimePrekeys, "OneTimePrekeys");
        ValidateKyberPrekeys(oneTimeKyberPrekeys, "OneTimeKyberPrekeys");

        if (lastResortKyberPrekey != null)
        {
            ValidateKyberPrekey(lastResortKyberPrekey, "LastResortKyberPrekey");
        }

        if (lastResortKyberPrekey == null && oneTimeKyberPrekeys.Count != 0)
        {
            throw new PrekeyException(StatusCode.InvalidArgument, "Kyber pool требует last-resort Kyber prekey");
        }

        if (lastResortKyberPrekey != null && oneTimeKyberPrekeys.Any(prekey => prekey.PrekeyId == lastResortKyberPrekey.PrekeyId))
        {
            throw new PrekeyException(StatusCode.InvalidArgument, "Last-resort и one-time Kyber prekeys должны иметь разные id");
        }
    }

    private static void ValidateEcPrekeys(IReadOnlyList<(long PrekeyId, byte[] PublicKey)> prekeys, string name)
    {
        PrekeyMaterialValidator.ValidateBatchCount(name, prekeys.Count);
        foreach (var prekey in prekeys)
        {
            PrekeyMaterialValidator.ValidateKey($"{name}.PublicKey", prekey.PublicKey);
        }

        CollapseEcPrekeys(prekeys);
    }

    private static void ValidateKyberPrekeys(IReadOnlyList<KyberPrekeyInput> prekeys, string name)
    {
        PrekeyMaterialValidator.ValidateBatchCount(name, prekeys.Count);
        foreach (var prekey in prekeys)
        {
            ValidateKyberPrekey(prekey, $"{name}[{prekey.PrekeyId}]");
        }

        CollapseKyberPrekeys(prekeys);
    }

    private static void ValidateKyberPrekey(KyberPrekeyInput prekey, string name)
    {
        PrekeyMaterialValidator.ValidateKey($"{name}.PublicKey", prekey.PublicKey);
        PrekeyMaterialValidator.ValidateSignature($"{name}.Signature", prekey.Signature);
    }

    private static IReadOnlyList<(long PrekeyId, byte[] PublicKey)> CollapseEcPrekeys(
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> prekeys)
    {
        var unique = new Dictionary<long, byte[]>();
        foreach (var prekey in prekeys)
        {
            if (unique.TryGetValue(prekey.PrekeyId, out var publicKey))
            {
                if (!publicKey.SequenceEqual(prekey.PublicKey))
                {
                    throw new PrekeyException(StatusCode.AlreadyExists, $"EC prekey id {prekey.PrekeyId} повторяется с другим ключом");
                }

                continue;
            }

            unique.Add(prekey.PrekeyId, prekey.PublicKey);
        }

        return unique.Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    private static IReadOnlyList<KyberPrekeyInput> CollapseKyberPrekeys(IReadOnlyList<KyberPrekeyInput> prekeys)
    {
        var unique = new Dictionary<long, KyberPrekeyInput>();
        foreach (var prekey in prekeys)
        {
            if (unique.TryGetValue(prekey.PrekeyId, out var existing))
            {
                if (!existing.PublicKey.SequenceEqual(prekey.PublicKey) || !existing.Signature.SequenceEqual(prekey.Signature))
                {
                    throw new PrekeyException(StatusCode.AlreadyExists, $"Kyber prekey id {prekey.PrekeyId} повторяется с другим payload");
                }

                continue;
            }

            unique.Add(prekey.PrekeyId, prekey);
        }

        return unique.Values.ToArray();
    }

    private static bool HasLastResortKyberPrekey(DevicePrekeyBundle bundle) =>
        bundle.LastResortKyberPrekeyId != null &&
        bundle.LastResortKyberPrekeyPublic != null &&
        bundle.LastResortKyberPrekeySignature != null;

    private async Task EnsureKyberIdIsNotInOneTimePoolAsync(Guid deviceId, long prekeyId, CancellationToken cancellationToken)
    {
        if (await context.OneTimeKyberPrekeys.AnyAsync(
                prekey => prekey.DeviceId == deviceId && prekey.PrekeyId == prekeyId,
                cancellationToken))
        {
            throw new PrekeyException(StatusCode.AlreadyExists, $"Kyber prekey id {prekeyId} уже используется one-time pool");
        }
    }

    private static PrekeyException DeviceOwnershipException() =>
        new(StatusCode.PermissionDenied, "Устройство не найдено или не принадлежит текущему пользователю");
}

public sealed record KyberPrekeyInput(long PrekeyId, byte[] PublicKey, byte[] Signature);

public sealed record PrekeyPoolCounts(int OneTimePrekeys, int OneTimeKyberPrekeys);

public sealed record FetchedPrekeyBundle(
    DevicePrekeyBundle Bundle,
    OneTimePrekey? EcPrekey,
    OneTimeKyberPrekey? KyberPrekey,
    bool KyberPrekeyIsLastResort,
    int RemainingEcPrekeys,
    int RemainingKyberPrekeys);

public sealed record OwnPrekeyStatus(
    DevicePrekeyBundle? Bundle,
    int RemainingEcPrekeys,
    int RemainingKyberPrekeys);
