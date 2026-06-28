using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using System.Net;
using System.Security.Claims;
using System.Text;

namespace BarkFluff.Identity.Tests;

public static class TestHelper
{
    public static IdentityContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityContext(options);
    }

    public static UserContext CreateUserContext(long userId, string? deviceId = null, TokenType tokenType = TokenType.User)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, tokenType.ToString()),
        };
        if (deviceId != null)
            claims.Add(new Claim(IdentityClaims.DeviceId, deviceId));

        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        return new UserContext(httpContextAccessor.Object);
    }

    public static LocationClient CreateLocationClient()
    {
        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        return new LocationClient(httpClient, new MetricsCollector(), Mock.Of<Microsoft.Extensions.Logging.ILogger<LocationClient>>());
    }

    public static JwtService CreateJwtService()
    {
        var settings = new JwtSettings
        {
            SecretKey = "test-secret-key-that-is-long-enough-for-hmac-sha256",
            Issuer = "BarkFluff",
            Audience = "BarkFluff",
            ExpiryMinutes = 30
        };
        return new JwtService(settings);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = "{\"country\":\"Russia\",\"regionName\":\"Moscow\",\"city\":\"Moscow\"}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
