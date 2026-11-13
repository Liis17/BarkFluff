using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.ListOtpVerification;
using BarkFluff.Identity.Features.ListOtpVerificationServer;
using BarkFluff.Identity.Features.DisableOtpVerificationServer;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Users;
using Grpc.Core;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class DisableOtpVerificationServerCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly Mock<ILogger<DisableOtpVerificationServerCommandHandler>> _logger;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;

    public DisableOtpVerificationServerCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _logger = new Mock<ILogger<DisableOtpVerificationServerCommandHandler>>();
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        SetupStatus(AccountStatus.Active);
    }

    private void SetupStatus(AccountStatus status)
    {
        _usersClient.Setup(client => client.GetAccountRestrictionAsync(
                It.IsAny<GetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetAccountRestrictionResponse>(
                Task.FromResult(new GetAccountRestrictionResponse { Restriction = new AccountRestrictionInfo { Status = status } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    private DisableOtpVerificationServerCommandHandler CreateHandler() =>
        new(_authPropsStorage, _usersClient.Object, _logger.Object);

    [Fact]
    public async Task Handle_AuthenticatorType_DisablesOtp()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationServerCommand { UserId = 1, OtpType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.OtpEnabled);
    }

    [Fact]
    public async Task Handle_EmailType_DisablesEmailOtp()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, EmailOtpEnabled = true });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationServerCommand { UserId = 1, OtpType = BarkFluff.Proto.Identity.OtpTypeId.Email };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.EmailOtpEnabled);
    }

    [Fact]
    public async Task Handle_DeletedAccount_RejectsBeforeDisablingOtp()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true });
        await _context.SaveChangesAsync();
        SetupStatus(AccountStatus.Deleted);

        var ex = await Assert.ThrowsAsync<RpcException>(() => CreateHandler().Handle(
            new DisableOtpVerificationServerCommand { UserId = 1, OtpType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator },
            CancellationToken.None));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.True((await _context.AuthUserProperties.SingleAsync()).OtpEnabled);
    }
}
