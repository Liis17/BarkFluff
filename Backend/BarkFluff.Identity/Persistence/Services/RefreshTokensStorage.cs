using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Services;

public class RefreshTokensStorage(IdentityContext context)
{
    public async Task<RefreshToken?> FindRefreshToken(string refreshToken)
    {
        var refreshTokenEntity = await context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Value == refreshToken);

        return refreshTokenEntity;
    }

    public async Task<RefreshToken?> CreateNewRefreshToken(string refreshToken, long userId, string deviceId, int expiresDays)
    {
        var refreshTokenEntity = new RefreshToken()
        {
            CreatedAt = DateTime.UtcNow,
            DeviceId = deviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(expiresDays),
            UserId = userId,
            Value = refreshToken
        };

        var token = await context.RefreshTokens.AddAsync(refreshTokenEntity);

        await context.SaveChangesAsync();

        return token.Entity;
    }

    public async Task<List<RefreshToken>> GetRefreshTokens(long userId)
    {
        return await context.RefreshTokens.Where(x => x.UserId == userId).ToListAsync();
    }

    public async Task DeleteRefreshTokensByDeviceId(string deviceId, long userId)
    {
        var refreshTokens = await context.RefreshTokens
            .Where(x => x.DeviceId == deviceId && x.UserId == userId)
            .ToListAsync();

        if (refreshTokens.Count == 0)
        {
            throw new RefreshTokenNotFoundException();
        }

        context.RefreshTokens.RemoveRange(refreshTokens);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Удаляет все refresh токены для устройства. Не выбрасывает исключение если токенов нет.
    /// Используется при логине для очистки старых токенов перед созданием нового.
    /// </summary>
    public async Task DeleteRefreshTokensByDeviceIdSafe(string deviceId, long userId)
    {
        var refreshTokens = await context.RefreshTokens
            .Where(x => x.DeviceId == deviceId && x.UserId == userId)
            .ToListAsync();

        if (refreshTokens.Count > 0)
        {
            context.RefreshTokens.RemoveRange(refreshTokens);
            await context.SaveChangesAsync();
        }
    }
}
