using BarkFluff.Identity.Features.GetBotTokenServer;
using BarkFluff.Identity.Services;
using BarkFluff.Shared.Exceptions.Bots;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using System.IdentityModel.Tokens.Jwt;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class GetBotTokenServerCommandHandlerTests
{
    private readonly GetBotTokenServerCommandHandler _handler;

    public GetBotTokenServerCommandHandlerTests()
    {
        var jwtService = new JwtService(new BarkFluff.Identity.Settings.JwtSettings
        {
            SecretKey = "test-secret-key-that-is-long-enough-for-hmac-sha256",
            Issuer = "BarkFluff",
            Audience = "BarkFluff",
            ExpiryMinutes = 30
        });
        _handler = new GetBotTokenServerCommandHandler(jwtService);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReissuesBotJwtWithSameTokenId()
    {
        var result = await _handler.Handle(new GetBotTokenServerCommand
        {
            BotUserId = 42,
            TokenId = "existing-token-id"
        }, CancellationToken.None);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal("42", jwt.Claims.First(c => c.Type == IdentityClaims.UserId).Value);
        Assert.Equal("Bot", jwt.Claims.First(c => c.Type == IdentityClaims.TokenType).Value);
        Assert.Equal("existing-token-id", jwt.Claims.First(c => c.Type == IdentityClaims.BotTokenId).Value);
    }

    [Theory]
    [InlineData(0, "token-id")]
    [InlineData(-1, "token-id")]
    public async Task Handle_InvalidBotId_Throws(long botId, string tokenId)
    {
        await Assert.ThrowsAsync<NotValidBotUserIdException>(() => _handler.Handle(
            new GetBotTokenServerCommand { BotUserId = botId, TokenId = tokenId },
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmptyTokenId_ThrowsInvalidArgument()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() => _handler.Handle(
            new GetBotTokenServerCommand { BotUserId = 42, TokenId = " " },
            CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }
}
