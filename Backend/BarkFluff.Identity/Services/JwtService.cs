using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Identity;

using Google.Protobuf.WellKnownTypes;

using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BarkFluff.Identity.Services;

public class JwtService(JwtSettings jwtSettings)
{
    public Token GenerateUserToken(long userId, string deviceId)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, TokenType.User.ToString()),
            new(IdentityClaims.DeviceId, deviceId),
        };

        var dateEnd = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

        var token = CreateToken(claims, dateEnd);

        return new Token { Value = token, ExpirationDate = Timestamp.FromDateTime(dateEnd) };
    }

    public string GenerateServerToken(ServiceId serviceId)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.ServiceId, serviceId.ToString()),
            new(IdentityClaims.TokenType, TokenType.Service.ToString()),
        };

        var dateEnd = new DateTime(9999, 12, 31, 23, 59, 59);

        var token = CreateToken(claims, dateEnd);

        return token;
    }

    private string CreateToken(IEnumerable<Claim> claims, DateTime expiration)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiration,
            Issuer = jwtSettings.Issuer,
            Audience = jwtSettings.Audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }
}