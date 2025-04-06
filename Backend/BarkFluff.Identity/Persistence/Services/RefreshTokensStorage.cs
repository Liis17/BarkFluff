using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
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

    public async Task<RefreshToken?> CreateNewRefreshToken(string refreshToken, long userId, string deviceName, int expiresDays)
    {
        var refreshTokenEntity = new RefreshToken()
        {
            CreatedAt = DateTime.UtcNow,
            DeviceName = deviceName,
            ExpiresAt = DateTime.UtcNow.AddDays(expiresDays),
            UserId = userId,
            Value = refreshToken
        };
        
        var token = await context.RefreshTokens.AddAsync(refreshTokenEntity);
        
        await context.SaveChangesAsync();
        
        return token.Entity;
    }
}