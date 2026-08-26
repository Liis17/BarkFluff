using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.ConfirmAccount;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class ConfirmAccountCommandHandlerTests
{
    private readonly ConfirmationCodesStorage _codesStorage;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly RequestContext _requestContext;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly LocationClient _locationClient;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<ConfirmAccountCommandHandler>> _logger;
    private readonly IdentityContext _context;

    public ConfirmAccountCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _codesStorage = new ConfirmationCodesStorage(_context);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _requestContext = new RequestContext { DeviceName = "TestDevice", DeviceId = "dev-1" };
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _locationClient = TestHelper.CreateLocationClient();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<ConfirmAccountCommandHandler>>();
    }

    private static RequestContext BuildRequestContext(
        string? deviceName = "TestDevice",
        string? deviceId = "dev-1") => new()
    {
        DeviceName = deviceName,
        DeviceId = deviceId
    };

    private ConfirmAccountCommandHandler CreateHandler(
        RequestContext? ctx = null,
        TestHelper.TestIdentityAbuseGuard? abuseGuard = null)
    {
        return new ConfirmAccountCommandHandler(
            _codesStorage, _usersClient.Object, _refreshTokensStorage,
            ctx ?? _requestContext, _notificationSender, _locationClient, _metrics, _logger.Object,
            abuseGuard ?? TestHelper.CreateAbuseGuard());
    }

    [Fact]
    public async Task Handle_NoDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(deviceName: null));
        var cmd = new ConfirmAccountCommand { Code = "123456", CodeId = Guid.NewGuid().ToString() };

        await Assert.ThrowsAsync<XDeviceNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CodeNotFound_ThrowsConfirmationCodeNotFoundException()
    {
        var handler = CreateHandler();
        var cmd = new ConfirmAccountCommand { Code = "123456", CodeId = Guid.NewGuid().ToString() };

        await Assert.ThrowsAsync<ConfirmationCodeNotFoundException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CodeWrongType_ThrowsConfirmationCodeNotFoundException()
    {
        _context.ConfirmationCodes.Add(new ConfirmationCode { Type = ConfirmationCodeType.Unknown, Expires = DateTime.UtcNow.AddHours(1), Value = "000000" });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmAccountCommand { Code = "123456", CodeId = _context.ConfirmationCodes.First().Id.ToString() };

        await Assert.ThrowsAsync<ConfirmationCodeNotFoundException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CodeExpired_ThrowsConfirmationCodeExpiredException()
    {
        _context.ConfirmationCodes.Add(new ConfirmationCode { Type = ConfirmationCodeType.Registration, Expires = DateTime.UtcNow.AddHours(-1), Value = "000000" });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmAccountCommand { Code = "123456", CodeId = _context.ConfirmationCodes.First().Id.ToString() };

        await Assert.ThrowsAsync<ConfirmationCodeExpiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WrongCode_ThrowsConfirmationCodeIncorrectException()
    {
        _context.ConfirmationCodes.Add(new ConfirmationCode
        {
            Type = ConfirmationCodeType.Registration,
            Expires = DateTime.UtcNow.AddHours(1),
            Value = "654321"
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmAccountCommand { Code = "123456", CodeId = _context.ConfirmationCodes.First().Id.ToString() };

        await Assert.ThrowsAsync<ConfirmationCodeIncorrectException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_FifthWrongCode_InvalidatesCodeAndStopsAccountConfirmation()
    {
        var codeId = Guid.NewGuid();
        _context.ConfirmationCodes.Add(new ConfirmationCode
        {
            Id = codeId,
            Type = ConfirmationCodeType.Registration,
            Expires = DateTime.UtcNow.AddHours(1),
            Value = "654321",
            OwnerId = 42
        });
        _context.SaveChanges();

        var abuseGuard = TestHelper.CreateAbuseGuard();
        abuseGuard.CodeFailureResult = new IdentityFailureResult(5, true);
        var handler = CreateHandler(abuseGuard: abuseGuard);

        await Assert.ThrowsAsync<IdentityLockoutException>(() => handler.Handle(
            new ConfirmAccountCommand { Code = "123456", CodeId = codeId.ToString() },
            CancellationToken.None));

        Assert.Empty(_context.ConfirmationCodes);
        _usersClient.Verify(
            c => c.ConfirmUserAsync(It.IsAny<ConfirmUserRequest>(), null, null, CancellationToken.None),
            Times.Never);
        Assert.Equal(1, abuseGuard.CodeFailureCalls);
    }

    [Fact]
    public async Task Handle_ValidCode_ConfirmsAccountAndReturnsRefreshToken()
    {
        var codeId = Guid.NewGuid();
        _context.ConfirmationCodes.Add(new ConfirmationCode
        {
            Id = codeId,
            Type = ConfirmationCodeType.Registration,
            Expires = DateTime.UtcNow.AddHours(1),
            Value = "123456",
            OwnerId = 42
        });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.ConfirmUserAsync(It.IsAny<ConfirmUserRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<ConfirmUserResponse>(
                Task.FromResult(new ConfirmUserResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(new GetByIdResponse { User = new User { Id = 42, Username = "user" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse { User = new User { Id = 42 }, Contact = new UserContact { Email = "test@test.com" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();
        var cmd = new ConfirmAccountCommand { Code = "123456", CodeId = codeId.ToString() };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RefreshToken);
        Assert.Empty(_context.ConfirmationCodes);
        _usersClient.Verify(c => c.ConfirmUserAsync(It.IsAny<ConfirmUserRequest>(), null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_CodeCaseInsensitive_Works()
    {
        var codeId = Guid.NewGuid();
        _context.ConfirmationCodes.Add(new ConfirmationCode
        {
            Id = codeId,
            Type = ConfirmationCodeType.Registration,
            Expires = DateTime.UtcNow.AddHours(1),
            Value = "ABC123",
            OwnerId = 1
        });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.ConfirmUserAsync(It.IsAny<ConfirmUserRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<ConfirmUserResponse>(
                Task.FromResult(new ConfirmUserResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

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

        var handler = CreateHandler();
        var cmd = new ConfirmAccountCommand { Code = "abc123", CodeId = codeId.ToString() };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_ValidCode_SendsNotification()
    {
        var codeId = Guid.NewGuid();
        _context.ConfirmationCodes.Add(new ConfirmationCode
        {
            Id = codeId,
            Type = ConfirmationCodeType.Registration,
            Expires = DateTime.UtcNow.AddHours(1),
            Value = "123456",
            OwnerId = 1
        });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.ConfirmUserAsync(It.IsAny<ConfirmUserRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<ConfirmUserResponse>(
                Task.FromResult(new ConfirmUserResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

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

        var handler = CreateHandler();
        var cmd = new ConfirmAccountCommand { Code = "123456", CodeId = codeId.ToString() };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
