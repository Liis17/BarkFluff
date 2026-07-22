using System.Security.Claims;

using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Services;
using BarkFluff.Bots.Tests.Fakes;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Bots.Tests.Services;

public class BotAccessValidatorTests
{
    private readonly BotRegistryCache _registry = new(Mock.Of<IBus>(), Mock.Of<ILogger<BotRegistryCache>>());
    private readonly BotAccessValidator _validator;

    public BotAccessValidatorTests()
    {
        _validator = new BotAccessValidator(_registry, new InMemoryBotRateLimiter());
    }

    [Fact]
    public async Task Validate_ValidBotToken_Allowed()
    {
        _registry.Load([new Bot { Id = 1, TokenId = "tid-1" }]);

        var status = await _validator.ValidateAsync(CreatePrincipal("Bot", 1, "tid-1"));

        Assert.Equal(BotAccessStatus.Allowed, status);
    }

    [Fact]
    public async Task Validate_ServiceToken_Unauthenticated()
    {
        _registry.Load([new Bot { Id = 1, TokenId = "tid-1" }]);

        var status = await _validator.ValidateAsync(CreatePrincipal("Service", 1, "tid-1"));

        Assert.Equal(BotAccessStatus.Unauthenticated, status);
    }

    [Fact]
    public async Task Validate_UnknownBot_Unauthenticated()
    {
        var status = await _validator.ValidateAsync(CreatePrincipal("Bot", 99, "tid-1"));

        Assert.Equal(BotAccessStatus.Unauthenticated, status);
    }

    [Fact]
    public async Task Validate_SystemBot_Unauthenticated()
    {
        _registry.Load([new Bot { Id = 1, TokenId = "tid-1", SystemRole = SystemBotRole.BotFather }]);

        var status = await _validator.ValidateAsync(CreatePrincipal("Bot", 1, "tid-1"));

        Assert.Equal(BotAccessStatus.Unauthenticated, status);
    }

    [Fact]
    public async Task Validate_RevokedTokenId_Unauthenticated()
    {
        _registry.Load([new Bot { Id = 1, TokenId = "tid-new" }]);

        var status = await _validator.ValidateAsync(CreatePrincipal("Bot", 1, "tid-old"));

        Assert.Equal(BotAccessStatus.Unauthenticated, status);
    }

    [Fact]
    public async Task Validate_MissingTokenIdClaim_Unauthenticated()
    {
        _registry.Load([new Bot { Id = 1, TokenId = "tid-1" }]);

        var status = await _validator.ValidateAsync(CreatePrincipal("Bot", 1));

        Assert.Equal(BotAccessStatus.Unauthenticated, status);
    }

    [Fact]
    public async Task Validate_RateLimitExceeded_RateLimited()
    {
        _registry.Load([new Bot { Id = 1, TokenId = "tid-1" }]);
        var principal = CreatePrincipal("Bot", 1, "tid-1");

        // 62 вызова гарантированно переполняют окно 30 req/s даже на границе секунд
        var statuses = new List<BotAccessStatus>();
        for (var i = 0; i < 62; i++)
            statuses.Add(await _validator.ValidateAsync(principal));

        Assert.Contains(BotAccessStatus.RateLimited, statuses);
    }

    private static ClaimsPrincipal CreatePrincipal(string tokenType, long? userId = null, string? tokenId = null)
    {
        var claims = new List<Claim> { new(IdentityClaims.TokenType, tokenType) };

        if (userId is not null)
            claims.Add(new Claim(IdentityClaims.UserId, userId.Value.ToString()));

        if (tokenId is not null)
            claims.Add(new Claim(IdentityClaims.BotTokenId, tokenId));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }
}
