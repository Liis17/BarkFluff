using BarkFluff.Identity.Features.CreateBotTokenServer;
using BarkFluff.Identity.Services;
using BarkFluff.Shared.Exceptions.Bots;
using BarkFluff.Shared.Identity;

using Microsoft.Extensions.Logging;

using Moq;

using System.IdentityModel.Tokens.Jwt;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class CreateBotTokenServerCommandHandlerTests
{
    private readonly CreateBotTokenServerCommandHandler _handler;

    public CreateBotTokenServerCommandHandlerTests()
    {
        var jwtService = new JwtService(new BarkFluff.Identity.Settings.JwtSettings
        {
            SecretKey = "test-secret-key-that-is-long-enough-for-hmac-sha256",
            Issuer = "BarkFluff",
            Audience = "BarkFluff",
            ExpiryMinutes = 30
        });
        var logger = new Mock<ILogger<CreateBotTokenServerCommandHandler>>();
        _handler = new CreateBotTokenServerCommandHandler(jwtService, logger.Object);
    }

    [Fact]
    public async Task Handle_ValidBotUserId_ReturnsTokenWithTokenId()
    {
        var result = await _handler.Handle(new CreateBotTokenServerCommand { BotUserId = 42 }, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.True(Guid.TryParse(result.TokenId, out _));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal("42", jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.UserId)?.Value);
        Assert.Equal("Bot", jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.TokenType)?.Value);
        Assert.Equal(result.TokenId, jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.BotTokenId)?.Value);
    }

    [Fact]
    public async Task Handle_EachCall_GeneratesUniqueTokenId()
    {
        var first = await _handler.Handle(new CreateBotTokenServerCommand { BotUserId = 1 }, CancellationToken.None);
        var second = await _handler.Handle(new CreateBotTokenServerCommand { BotUserId = 1 }, CancellationToken.None);

        Assert.NotEqual(first.TokenId, second.TokenId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_InvalidBotUserId_Throws(long botUserId)
    {
        await Assert.ThrowsAsync<NotValidBotUserIdException>(
            () => _handler.Handle(new CreateBotTokenServerCommand { BotUserId = botUserId }, CancellationToken.None));
    }
}
