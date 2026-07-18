using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class DevicesStorage(UsersContext context)
{
    public async Task<UserDevice> RegisterOrUpdateDevice(Guid deviceId, long userId, string originalName,
        string? appName, string? operationSystem, string? location)
    {
        var authorizedAt = DateTime.UtcNow;

        if (context.Database.ProviderName is "Npgsql.EntityFrameworkCore.PostgreSQL"
            or "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // DeviceId globally identifies an app installation; re-authentication transfers it to the current user.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "UserDevices"
                    ("Id", "UserId", "OriginalName", "AuthorizedAt", "AppName", "OperationSystem", "Location", "NotificationsEnabled")
                VALUES
                    ({deviceId}, {userId}, {originalName}, {authorizedAt}, {appName}, {operationSystem}, {location}, {true})
                ON CONFLICT ("Id") DO UPDATE SET
                    "UserId" = EXCLUDED."UserId",
                    "OriginalName" = EXCLUDED."OriginalName",
                    "AuthorizedAt" = EXCLUDED."AuthorizedAt",
                    "AppName" = EXCLUDED."AppName",
                    "OperationSystem" = EXCLUDED."OperationSystem",
                    "Location" = EXCLUDED."Location"
                """);

            var tracked = context.UserDevices.Local.FirstOrDefault(d => d.Id == deviceId);
            if (tracked != null)
                context.Entry(tracked).State = EntityState.Detached;

            return await context.UserDevices.AsNoTracking().SingleAsync(d => d.Id == deviceId);
        }

        var existing = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (existing != null)
        {
            existing.UserId = userId;
            existing.OriginalName = originalName;
            existing.AppName = appName;
            existing.OperationSystem = operationSystem;
            existing.Location = location;
            existing.AuthorizedAt = authorizedAt;

            await context.SaveChangesAsync();
            return existing;
        }

        var device = new UserDevice
        {
            Id = deviceId,
            UserId = userId,
            OriginalName = originalName,
            AuthorizedAt = authorizedAt,
            AppName = appName,
            OperationSystem = operationSystem,
            Location = location
        };

        await context.UserDevices.AddAsync(device);
        await context.SaveChangesAsync();

        return device;
    }

    public async Task<bool> UpdateDeviceAppInfoIfChanged(Guid deviceId, long userId, string originalName, string? appName)
    {
        var existing = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (existing == null)
            return false; // устройство ещё не зарегистрировано — нечего обновлять

        if (existing.OriginalName == originalName && existing.AppName == appName)
            return false; // не изменилось — не пишем

        existing.OriginalName = originalName;
        existing.AppName = appName;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<List<UserDevice>> GetDevicesByUserId(long userId)
    {
        return await context.UserDevices
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.AuthorizedAt)
            .ToListAsync();
    }

    public async Task<UserDevice?> GetDeviceById(Guid deviceId, long userId)
    {
        return await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);
    }

    public async Task RenameDevice(Guid deviceId, long userId, string customName)
    {
        var device = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (device == null)
            throw new InvalidOperationException("Устройство не найдено");

        device.CustomName = customName;
        await context.SaveChangesAsync();
    }

    public async Task DeleteDevice(Guid deviceId, long userId)
    {
        var device = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (device == null)
            return;

        context.UserDevices.Remove(device);
        await context.SaveChangesAsync();
    }

    public async Task SetFirebaseToken(Guid deviceId, long userId, string token)
    {
        var device = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (device == null)
            throw new InvalidOperationException("Устройство не найдено");

        device.FirebaseDeviceToken = token;
        await context.SaveChangesAsync();
    }

    public async Task<List<(long UserId, string DeviceId, string FirebaseToken)>> GetDevicesWithFirebaseTokens(List<long> userIds, Guid? mutedChatFilter = null)
    {
        var now = DateTime.UtcNow;
        var query = context.UserDevices
            .Where(d => userIds.Contains(d.UserId) && d.FirebaseDeviceToken != null && d.NotificationsEnabled);

        // Исключаем пользователей, замьютивших этот чат (активный mute).
        if (mutedChatFilter is Guid chatId)
        {
            query = query.Where(d => !context.ChatMutes.Any(m =>
                m.UserId == d.UserId && m.ChatId == chatId
                && (m.MutedUntil == null || m.MutedUntil > now)));
        }

        return await query
            .Select(d => new ValueTuple<long, string, string>(d.UserId, d.Id.ToString(), d.FirebaseDeviceToken!))
            .ToListAsync();
    }

    public async Task<List<(long UserId, string DeviceId, string FirebaseToken)>> GetDevicesWithFirebaseTokensByDeviceIds(List<Guid> deviceIds)
    {
        return await context.UserDevices
            .Where(d => deviceIds.Contains(d.Id) && d.FirebaseDeviceToken != null && d.NotificationsEnabled)
            .Select(d => new ValueTuple<long, string, string>(d.UserId, d.Id.ToString(), d.FirebaseDeviceToken!))
            .ToListAsync();
    }

    public async Task<List<(long UserId, string DeviceId, string FirebaseToken)>> GetAllDevicesWithFirebaseTokens()
    {
        return await context.UserDevices
            .Where(d => d.FirebaseDeviceToken != null && d.NotificationsEnabled)
            .Select(d => new ValueTuple<long, string, string>(d.UserId, d.Id.ToString(), d.FirebaseDeviceToken!))
            .ToListAsync();
    }

    public async Task SetNotificationsEnabled(Guid deviceId, long userId, bool enabled)
    {
        var device = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (device == null)
            throw new InvalidOperationException("Устройство не найдено");

        device.NotificationsEnabled = enabled;
        await context.SaveChangesAsync();
    }
}
