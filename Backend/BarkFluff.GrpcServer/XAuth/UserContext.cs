using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Http;

namespace BarkFluff.GrpcServer.XAuth;

public class UserContext
{
    public long UserId { get; }

    public TokenType TokenType { get; }

    public string? DeviceId { get; }

    public bool IsAuthenticated => UserId != 0 && TokenType != TokenType.Unknown;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            UserId = long.Parse(principal.FindFirst(IdentityClaims.UserId)?.Value ?? "0");

            var tokenTypeValue = principal.FindFirst(IdentityClaims.TokenType)?.Value;

            Enum.TryParse(tokenTypeValue, out TokenType type);
            TokenType = type;

            DeviceId = principal.FindFirst(IdentityClaims.DeviceId)?.Value;
        }
    }
}