using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Features.CreateAccount;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class CreateAccountCommandHandlerTests
{
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly ConfirmationCodesStorage _codesStorage;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly RequestContext _requestContext;
    private readonly LocationClient _locationClient;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<CreateAccountCommandHandler>> _logger;
    private readonly IdentityContext _context;

    public CreateAccountCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _requestContext = new RequestContext
        {
            DeviceName = "TestDevice", OperationSystem = "Windows",
            AppName = "BF", AppVersion = "1.0", IpAddress = "127.0.0.1"
        };
        _locationClient = TestHelper.CreateLocationClient();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<CreateAccountCommandHandler>>();

        _context = TestHelper.CreateContext();
        _codesStorage = new ConfirmationCodesStorage(_context);
    }

    private static RequestContext BuildRequestContext(
        string? deviceName = "TestDevice",
        string? os = "Windows",
        string? appName = "BF",
        string? appVersion = "1.0",
        string? ipAddress = "127.0.0.1") => new()
    {
        DeviceName = deviceName,
        OperationSystem = os,
        AppName = appName,
        AppVersion = appVersion,
        IpAddress = ipAddress
    };

    private CreateAccountCommandHandler CreateHandler(RequestContext? ctx = null)
    {
        return new CreateAccountCommandHandler(
            _usersClient.Object, _codesStorage, _notificationSender,
            ctx ?? _requestContext, _locationClient, _metrics, _logger.Object);
    }

    [Fact]
    public async Task Handle_EmptyEmail_ThrowsUsernameOrEmailIsEmptyException()
    {
        var handler = CreateHandler();
        var cmd = new CreateAccountCommand { Username = "user", Email = "", FirstName = "First", LastName = "Last" };

        await Assert.ThrowsAsync<UsernameOrEmailIsEmptyException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmptyUsername_ThrowsUsernameOrEmailIsEmptyException()
    {
        var handler = CreateHandler();
        var cmd = new CreateAccountCommand { Username = "", Email = "test@test.com", FirstName = "First", LastName = "Last" };

        await Assert.ThrowsAsync<UsernameOrEmailIsEmptyException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(deviceName: null));
        var cmd = new CreateAccountCommand { Username = "user", Email = "test@test.com", FirstName = "F", LastName = "L" };

        await Assert.ThrowsAsync<XDeviceNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoOs_ThrowsXOsNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(os: null));
        var cmd = new CreateAccountCommand { Username = "user", Email = "test@test.com", FirstName = "F", LastName = "L" };

        await Assert.ThrowsAsync<XOsNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoAppInfo_ThrowsXAppInfoIsRequiedException()
    {
        var handler = CreateHandler(BuildRequestContext(appName: null));
        var cmd = new CreateAccountCommand { Username = "user", Email = "test@test.com", FirstName = "F", LastName = "L" };

        await Assert.ThrowsAsync<XAppInfoIsRequiedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NewUser_CreatesAccountAndSendsEmail()
    {
        _usersClient
            .Setup(c => c.AddDraftUserAsync(It.IsAny<AddDraftUserRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<AddDraftUserResponse>(
                Task.FromResult(new AddDraftUserResponse { UserId = 42 }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();
        var cmd = new CreateAccountCommand { Username = "user", Email = "test@test.com", FirstName = "First", LastName = "Last" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result.CodeId);
        Assert.NotEmpty(result.CodeId);
        Assert.Single(_context.ConfirmationCodes);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DraftUserExists_OverridesAndSucceeds()
    {
        _usersClient
            .Setup(c => c.AddDraftUserAsync(It.IsAny<AddDraftUserRequest>(), null, null, CancellationToken.None))
            .Throws(new UserIsDraftException());

        _usersClient
            .Setup(c => c.OverrideDraftUserAsync(It.IsAny<AddDraftUserRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<AddDraftUserResponse>(
                Task.FromResult(new AddDraftUserResponse { UserId = 42 }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();
        var cmd = new CreateAccountCommand { Username = "user", Email = "test@test.com", FirstName = "First", LastName = "Last" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result.CodeId);
        _usersClient.Verify(c => c.OverrideDraftUserAsync(It.IsAny<AddDraftUserRequest>(), null, null, CancellationToken.None), Times.Once);
    }
}
