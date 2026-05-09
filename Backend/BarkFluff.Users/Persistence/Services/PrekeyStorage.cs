using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class PrekeyStorage(UsersContext context)
{
    public async Task<DevicePrekeyBundle> RegisterBundleAsync(
        Guid deviceId,
        long userId,
        long registrationId,
        byte[] identityPubkey,
        long signedPrekeyId,
        byte[] signedPrekeyPublic,
        byte[] signedPrekeySignature,
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> oneTimePrekeys)
    {
        var device = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (device == null)
        {
            throw new InvalidOperationException("Устройство не найдено");
        }

        var existing = await context.DevicePrekeyBundles
            .FirstOrDefaultAsync(b => b.DeviceId == deviceId);

        var now = DateTime.UtcNow;

        if (existing == null)
        {
            existing = new DevicePrekeyBundle
            {
                DeviceId = deviceId,
                RegistrationId = registrationId,
                IdentityPubkey = identityPubkey,
                SignedPrekeyId = signedPrekeyId,
                SignedPrekeyPublic = signedPrekeyPublic,
                SignedPrekeySignature = signedPrekeySignature,
                SignedPrekeyRotatedAt = now,
                CreatedAt = now,
            };
            await context.DevicePrekeyBundles.AddAsync(existing);
        }
        else
        {
            existing.RegistrationId = registrationId;
            existing.IdentityPubkey = identityPubkey;
            existing.SignedPrekeyId = signedPrekeyId;
            existing.SignedPrekeyPublic = signedPrekeyPublic;
            existing.SignedPrekeySignature = signedPrekeySignature;
            existing.SignedPrekeyRotatedAt = now;
        }

        if (oneTimePrekeys.Count > 0)
        {
            var existingIds = await context.OneTimePrekeys
                .Where(p => p.DeviceId == deviceId)
                .Select(p => p.PrekeyId)
                .ToListAsync();

            var existingSet = existingIds.ToHashSet();

            foreach (var (prekeyId, publicKey) in oneTimePrekeys)
            {
                if (existingSet.Contains(prekeyId))
                {
                    continue;
                }

                await context.OneTimePrekeys.AddAsync(new OneTimePrekey
                {
                    DeviceId = deviceId,
                    PrekeyId = prekeyId,
                    PublicKey = publicKey,
                    CreatedAt = now,
                });
            }
        }

        await context.SaveChangesAsync();
        return existing;
    }

    public async Task RotateSignedPrekeyAsync(
        Guid deviceId,
        long userId,
        long signedPrekeyId,
        byte[] signedPrekeyPublic,
        byte[] signedPrekeySignature)
    {
        var bundle = await context.DevicePrekeyBundles
            .Where(b => b.DeviceId == deviceId && b.Device.UserId == userId)
            .FirstOrDefaultAsync();

        if (bundle == null)
        {
            throw new InvalidOperationException("Bundle устройства не зарегистрирован");
        }

        bundle.SignedPrekeyId = signedPrekeyId;
        bundle.SignedPrekeyPublic = signedPrekeyPublic;
        bundle.SignedPrekeySignature = signedPrekeySignature;
        bundle.SignedPrekeyRotatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
    }

    public async Task<int> ReplenishOneTimePrekeysAsync(
        Guid deviceId,
        long userId,
        IReadOnlyList<(long PrekeyId, byte[] PublicKey)> prekeys)
    {
        var device = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (device == null)
        {
            throw new InvalidOperationException("Устройство не найдено");
        }

        if (prekeys.Count > 0)
        {
            var existingIds = await context.OneTimePrekeys
                .Where(p => p.DeviceId == deviceId)
                .Select(p => p.PrekeyId)
                .ToListAsync();

            var existingSet = existingIds.ToHashSet();
            var now = DateTime.UtcNow;

            foreach (var (prekeyId, publicKey) in prekeys)
            {
                if (existingSet.Contains(prekeyId))
                {
                    continue;
                }

                await context.OneTimePrekeys.AddAsync(new OneTimePrekey
                {
                    DeviceId = deviceId,
                    PrekeyId = prekeyId,
                    PublicKey = publicKey,
                    CreatedAt = now,
                });
            }

            await context.SaveChangesAsync();
        }

        return await context.OneTimePrekeys.CountAsync(p => p.DeviceId == deviceId);
    }

    /// <summary>
    /// Атомарно получает bundle устройства собеседника и расходует одну one-time prekey.
    /// FOR UPDATE SKIP LOCKED гарантирует, что параллельные запросы не получат одну и ту же prekey.
    /// </summary>
    public async Task<(DevicePrekeyBundle Bundle, OneTimePrekey? Prekey, int Remaining)?> FetchBundleAsync(
        long peerUserId,
        Guid peerDeviceId)
    {
        var bundle = await context.DevicePrekeyBundles
            .Where(b => b.DeviceId == peerDeviceId && b.Device.UserId == peerUserId)
            .FirstOrDefaultAsync();

        if (bundle == null)
        {
            return null;
        }

        // FromSqlInterpolated(DELETE ... RETURNING) — non-composable, EF8/9 не позволяет
        // добавлять FirstOrDefaultAsync/AsNoTracking сверху (LIMIT 1 не композируется).
        // Подзапрос уже гарантирует максимум одну строку, поэтому ToListAsync + FirstOrDefault.
        var prekeyList = await context.OneTimePrekeys
            .FromSqlInterpolated($@"
                DELETE FROM ""OneTimePrekeys""
                WHERE ""Id"" = (
                    SELECT ""Id"" FROM ""OneTimePrekeys""
                    WHERE ""DeviceId"" = {peerDeviceId}
                    ORDER BY ""Id""
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *")
            .ToListAsync();
        var prekey = prekeyList.FirstOrDefault();

        var remaining = await context.OneTimePrekeys.CountAsync(p => p.DeviceId == peerDeviceId);

        return (bundle, prekey, remaining);
    }

    public async Task<List<(UserDevice Device, bool HasBundle)>> ListPeerDevicesAsync(long peerUserId)
    {
        var devices = await context.UserDevices
            .Where(d => d.UserId == peerUserId)
            .OrderByDescending(d => d.AuthorizedAt)
            .ToListAsync();

        if (devices.Count == 0)
        {
            return new List<(UserDevice, bool)>();
        }

        var deviceIds = devices.Select(d => d.Id).ToList();

        var bundleDeviceIds = await context.DevicePrekeyBundles
            .Where(b => deviceIds.Contains(b.DeviceId))
            .Select(b => b.DeviceId)
            .ToListAsync();

        var bundleSet = bundleDeviceIds.ToHashSet();

        return devices
            .Select(d => (d, bundleSet.Contains(d.Id)))
            .ToList();
    }
}
