using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Features.Auth;
using BarkFluff.Identity.Features.CreateSessionForUserServer;
using BarkFluff.Identity.Features.ResetPassword;
using BarkFluff.Identity.Security;
using BarkFluff.Proto.Identity;

using Xunit;

namespace BarkFluff.Identity.Tests.Security;

public class IdentityAbuseGuardBehaviorTests
{
    [Fact]
    public async Task PublicHighRiskCommand_UsesTrustedIpAndGuard()
    {
        var abuseGuard = TestHelper.CreateAbuseGuard();
        var behavior = new IdentityAbuseGuardBehavior<AuthCommand, AuthResponse>(
            abuseGuard,
            new RequestContext { TrustedIpAddress = "198.51.100.10" },
            TestHelper.CreateUserContext(42));

        var nextCalled = false;
        await behavior.Handle(
            new AuthCommand { Username = "alice", Password = "password" },
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(new AuthResponse());
            },
            CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Equal(1, abuseGuard.RequestAllowedCalls);
        Assert.Equal(IdentityAbuseOperation.Auth, abuseGuard.LastOperation);
        Assert.Equal("198.51.100.10", abuseGuard.LastTrustedIpAddress);
        Assert.False(abuseGuard.LastCountSubject);
    }

    [Fact]
    public async Task ServerCommand_DoesNotUseUserLockoutGuard()
    {
        var abuseGuard = TestHelper.CreateAbuseGuard();
        var behavior = new IdentityAbuseGuardBehavior<CreateSessionForUserServerCommand, CreateSessionForUserServerResponse>(
            abuseGuard,
            new RequestContext { TrustedIpAddress = "198.51.100.10" },
            TestHelper.CreateUserContext(42));

        await behavior.Handle(
            new CreateSessionForUserServerCommand { UserId = 42 },
            _ => Task.FromResult(new CreateSessionForUserServerResponse()),
            CancellationToken.None);

        Assert.Equal(0, abuseGuard.RequestAllowedCalls);
    }

    [Fact]
    public async Task ResetPassword_WithOnlyUsername_UsesUsernameAsSubject()
    {
        var abuseGuard = TestHelper.CreateAbuseGuard();
        var behavior = new IdentityAbuseGuardBehavior<ResetPasswordCommand, ResetPasswordResponse>(
            abuseGuard,
            new RequestContext { TrustedIpAddress = "198.51.100.10" },
            TestHelper.CreateUserContext(42));

        await behavior.Handle(
            new ResetPasswordCommand { Email = string.Empty, Username = "alice" },
            _ => Task.FromResult(new ResetPasswordResponse()),
            CancellationToken.None);

        Assert.Equal("alice", abuseGuard.LastSubject);
        Assert.True(abuseGuard.LastCountSubject);
    }
}
