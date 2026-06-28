using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.SetPassword;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class SetPasswordCommandHandlerTests
{
    private readonly UserContext _userContext;
    private readonly IdentityContext _context;
    private readonly PasswordsStorage _passwordsStorage;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly LocationClient _locationClient;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly RequestContext _requestContext;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<SetPasswordCommandHandler>> _logger;

    public SetPasswordCommandHandlerTests()
    {
        _userContext = TestHelper.CreateUserContext(1);
        _context = TestHelper.CreateContext();
        _passwordsStorage = new PasswordsStorage(_context);
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _locationClient = TestHelper.CreateLocationClient();
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _requestContext = new RequestContext { DeviceName = "Dev", IpAddress = "1.1.1.1", AppName = "BF", AppVersion = "1.0", OperationSystem = "Win" };
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<SetPasswordCommandHandler>>();

        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(new GetByIdResponse { User = new User { Id = 1, Username = "user" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse { User = new User { Id = 1 }, Contact = new UserContact { Email = "t@t.com" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    private SetPasswordCommandHandler CreateHandler()
    {
        return new SetPasswordCommandHandler(
            _userContext, _passwordsStorage, _refreshTokensStorage,
            _notificationSender, _locationClient, _usersClient.Object,
            _requestContext, _metrics, _logger.Object);
    }

    [Fact]
    public async Task Handle_NoExistingPassword_SetsNewPassword()
    {
        var handler = CreateHandler();
        var cmd = new SetPasswordCommand { NewPassword = "newpass123" };

        await handler.Handle(cmd, CancellationToken.None);

        var pw = await _context.UserPasswords.FirstOrDefaultAsync(x => x.UserId == 1);
        Assert.NotNull(pw);
        Assert.NotNull(pw.PasswordHash);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Notifications.EmailNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingPassword_NoOldPassword_ThrowsInvalidOldPasswordException()
    {
        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("oldpass") });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new SetPasswordCommand { NewPassword = "newpass", OldPassword = "" };

        await Assert.ThrowsAsync<InvalidOldPasswordException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ExistingPassword_WrongOldPassword_ThrowsInvalidOldPasswordException()
    {
        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("correctold") });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new SetPasswordCommand { NewPassword = "newpass", OldPassword = "wrongold" };

        await Assert.ThrowsAsync<InvalidOldPasswordException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ExistingPassword_CorrectOldPassword_UpdatesAndSendsNotification()
    {
        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("correctold") });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new SetPasswordCommand { NewPassword = "newpass123", OldPassword = "correctold" };

        await handler.Handle(cmd, CancellationToken.None);

        var pw = await _context.UserPasswords.FirstOrDefaultAsync(x => x.UserId == 1);
        Assert.NotNull(pw);
        Assert.True(PasswordHasher.VerifyPassword("newpass123", pw.PasswordHash));
        Assert.False(PasswordHasher.VerifyPassword("correctold", pw.PasswordHash));
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<BarkFluff.Shared.Queue.Notifications.EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
