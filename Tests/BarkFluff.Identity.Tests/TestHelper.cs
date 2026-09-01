using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Security;
using BarkFluff.Identity.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Shared.Exceptions.Identity;
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
    public static TestIdentityAbuseGuard CreateAbuseGuard() => new();

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

    public sealed class TestIdentityAbuseGuard : IIdentityAbuseGuard
    {
        public IdentityFailureResult LoginFailureResult { get; set; } = new(1, false);

        public IdentityFailureResult CodeFailureResult { get; set; } = new(1, false);

        public IdentityFailureResult OtpFailureResult { get; set; } = new(1, false);

        public int LoginFailureCalls { get; private set; }

        public int CodeFailureCalls { get; private set; }

        public int OtpFailureCalls { get; private set; }

        public int ClearLoginFailuresCalls { get; private set; }

        public int ClearCodeFailuresCalls { get; private set; }

        public int ClearOtpFailuresCalls { get; private set; }

        public int SubjectRequestCalls { get; private set; }

        public int RequestAllowedCalls { get; private set; }

        public IdentityAbuseOperation? LastOperation { get; private set; }

        public string? LastTrustedIpAddress { get; private set; }

        public string? LastSubject { get; private set; }

        public bool LastCountSubject { get; private set; }

        private bool _loginLocked;
        private bool _userLocked;
        private bool _codeLocked;
        private bool _otpLocked;

        public Task EnsureRequestAllowedAsync(IdentityAbuseOperation operation, string? trustedIpAddress, string? subject,
            bool countSubject, CancellationToken cancellationToken = default)
        {
            RequestAllowedCalls++;
            LastOperation = operation;
            LastTrustedIpAddress = trustedIpAddress;
            LastSubject = subject;
            LastCountSubject = countSubject;
            return Task.CompletedTask;
        }

        public Task EnsureSubjectRequestAllowedAsync(IdentityAbuseOperation operation, string subject,
            CancellationToken cancellationToken = default)
        {
            SubjectRequestCalls++;
            return Task.CompletedTask;
        }

        public Task EnsureUserAllowedAsync(long userId, CancellationToken cancellationToken = default)
        {
            if (_userLocked)
                throw new IdentityLockoutException();

            return Task.CompletedTask;
        }

        public Task EnsureLoginAllowedAsync(string login, string? trustedIpAddress,
            CancellationToken cancellationToken = default)
        {
            if (_loginLocked)
                throw new IdentityLockoutException();

            return Task.CompletedTask;
        }

        public Task<IdentityFailureResult> RegisterLoginFailureAsync(string login, string? trustedIpAddress, long? userId,
            CancellationToken cancellationToken = default)
        {
            LoginFailureCalls++;
            if (LoginFailureResult.Locked)
            {
                _loginLocked = true;
                if (userId.HasValue)
                    _userLocked = true;
            }

            return Task.FromResult(LoginFailureResult);
        }

        public Task ClearLoginFailuresAsync(string login, string? trustedIpAddress, long userId,
            CancellationToken cancellationToken = default)
        {
            ClearLoginFailuresCalls++;
            _loginLocked = false;
            _userLocked = false;
            return Task.CompletedTask;
        }

        public Task EnsureCodeAllowedAsync(IdentityCodeKind codeKind, Guid codeId,
            CancellationToken cancellationToken = default)
        {
            if (_codeLocked)
                throw new IdentityLockoutException();

            return Task.CompletedTask;
        }

        public Task<IdentityFailureResult> RegisterCodeFailureAsync(IdentityCodeKind codeKind, Guid codeId, DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            CodeFailureCalls++;
            if (CodeFailureResult.Locked)
                _codeLocked = true;

            return Task.FromResult(CodeFailureResult);
        }

        public Task ClearCodeFailuresAsync(IdentityCodeKind codeKind, Guid codeId,
            CancellationToken cancellationToken = default)
        {
            ClearCodeFailuresCalls++;
            _codeLocked = false;
            return Task.CompletedTask;
        }

        public Task EnsureOtpOperationAllowedAsync(IdentityOtpOperation operation, long userId,
            CancellationToken cancellationToken = default)
        {
            if (_otpLocked)
                throw new IdentityLockoutException();

            return Task.CompletedTask;
        }

        public Task<IdentityFailureResult> RegisterOtpFailureAsync(IdentityOtpOperation operation, long userId,
            CancellationToken cancellationToken = default)
        {
            OtpFailureCalls++;
            if (OtpFailureResult.Locked)
                _otpLocked = true;

            return Task.FromResult(OtpFailureResult);
        }

        public Task ClearOtpFailuresAsync(IdentityOtpOperation operation, long userId,
            CancellationToken cancellationToken = default)
        {
            ClearOtpFailuresCalls++;
            _otpLocked = false;
            return Task.CompletedTask;
        }

        public Task DelayAfterFailureAsync(int attempts, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
