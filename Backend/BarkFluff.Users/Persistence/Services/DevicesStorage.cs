using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class DevicesStorage(UsersContext context)
{
    public async Task<UserDevice> RegisterOrUpdateDevice(Guid deviceId, long userId, string originalName,
        string? appName, string? operationSystem, string? location)
    {
        var existing = await context.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

        if (existing != null)
        {
            existing.OriginalName = originalName;
            existing.AppName = appName;
            existing.OperationSystem = operationSystem;
            existing.Location = location;
            existing.AuthorizedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return existing;
        }

        var device = new UserDevice
        {
            Id = deviceId,
            UserId = userId,
            OriginalName = originalName,
            AuthorizedAt = DateTime.UtcNow,
            AppName = appName,
            OperationSystem = operationSystem,
            Location = location
        };

        await context.UserDevices.AddAsync(device);
        await context.SaveChangesAsync();

        return device;
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

    public async Task<List<(long UserId, string DeviceId, string FirebaseToken)>> GetDevicesWithFirebaseTokens(List<long> userIds)
    {
        return await context.UserDevices
            .Where(d => userIds.Contains(d.UserId) && d.FirebaseDeviceToken != null)
            .Select(d => new ValueTuple<long, string, string>(d.UserId, d.Id.ToString(), d.FirebaseDeviceToken!))
            .ToListAsync();
    }
}
