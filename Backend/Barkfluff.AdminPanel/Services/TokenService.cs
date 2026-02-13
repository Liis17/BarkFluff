using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel.Services;

public class TokenService
{
    private readonly TokenDbContext _db;
    private readonly IOptions<AuthSettings> _settings;

    public TokenService(TokenDbContext db, IOptions<AuthSettings> settings)
    {
        _db = db;
        _settings = settings;
    }

    public Guid CreateToken(string? ipAddress, string? userAgent, string? name)
    {
        var token = new AuthToken
        {
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Name = name ?? $"Session {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
        };

        _db.Tokens.Insert(token);
        return token.Id;
    }

    /// <summary>
    /// Creates a token with admin association
    /// </summary>
    public Guid CreateToken(string? ipAddress, string? userAgent, string? name, string? adminUsername, long? approvedByTelegramUserId)
    {
        var token = new AuthToken
        {
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Name = name ?? $"Session {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            AdminUsername = adminUsername,
            ApprovedByTelegramUserId = approvedByTelegramUserId
        };

        _db.Tokens.Insert(token);
        return token.Id;
    }

    public AuthToken? ValidateToken(Guid tokenId)
    {
        var token = _db.Tokens.FindById(tokenId);
        if (token == null) return null;

        if (token.IsExpired(_settings.Value.TokenExpirationDays))
        {
            _db.Tokens.Delete(tokenId);
            return null;
        }

        UpdateActivity(tokenId);
        return token;
    }

    public void UpdateActivity(Guid tokenId)
    {
        var token = _db.Tokens.FindById(tokenId);
        if (token != null)
        {
            token.LastActivity = DateTime.UtcNow;
            _db.Tokens.Update(token);
        }
    }

    public bool DeleteToken(Guid tokenId)
    {
        return _db.Tokens.Delete(tokenId);
    }

    /// <summary>
    /// Deletes a token only if it belongs to the specified admin
    /// </summary>
    public bool DeleteTokenByAdmin(Guid tokenId, long telegramUserId)
    {
        var token = _db.Tokens.FindById(tokenId);
        if (token == null) return false;

        if (token.ApprovedByTelegramUserId != telegramUserId)
            return false;

        return _db.Tokens.Delete(tokenId);
    }

    public bool RenameToken(Guid tokenId, string newName)
    {
        var token = _db.Tokens.FindById(tokenId);
        if (token == null) return false;

        token.Name = newName;
        return _db.Tokens.Update(token);
    }

    /// <summary>
    /// Renames a token only if it belongs to the specified admin
    /// </summary>
    public bool RenameTokenByAdmin(Guid tokenId, string newName, long telegramUserId)
    {
        var token = _db.Tokens.FindById(tokenId);
        if (token == null) return false;

        if (token.ApprovedByTelegramUserId != telegramUserId)
            return false;

        token.Name = newName;
        return _db.Tokens.Update(token);
    }

    public List<AuthToken> GetAllTokens()
    {
        return _db.Tokens.Query()
            .OrderByDescending(x => x.LastActivity)
            .ToList();
    }

    /// <summary>
    /// Gets all tokens that belong to a specific admin
    /// </summary>
    public List<AuthToken> GetTokensByAdmin(long telegramUserId)
    {
        return _db.Tokens.Query()
            .Where(x => x.ApprovedByTelegramUserId == telegramUserId)
            .OrderByDescending(x => x.LastActivity)
            .ToList();
    }

    public AuthToken? GetToken(Guid tokenId)
    {
        return _db.Tokens.FindById(tokenId);
    }

    public void CleanupExpiredTokens()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.Value.TokenExpirationDays);
        _db.Tokens.DeleteMany(x => x.LastActivity < cutoffDate);
    }
}
