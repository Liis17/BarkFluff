using Barkfluff.Docker.Control.Data;
using Barkfluff.Docker.Control.Models;
using Microsoft.Extensions.Options;

namespace Barkfluff.Docker.Control.Services;

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

    public bool RenameToken(Guid tokenId, string newName)
    {
        var token = _db.Tokens.FindById(tokenId);
        if (token == null) return false;

        token.Name = newName;
        return _db.Tokens.Update(token);
    }

    public List<AuthToken> GetAllTokens()
    {
        return _db.Tokens.Query()
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
