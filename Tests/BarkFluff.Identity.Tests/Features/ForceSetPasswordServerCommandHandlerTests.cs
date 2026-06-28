using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Features.ForceSetPasswordServer;
using BarkFluff.Identity.Features.CreateSessionForUserServer;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class ForceSetPasswordServerCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly PasswordsStorage _passwordsStorage;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<ILogger<ForceSetPasswordServerCommandHandler>> _logger;

    public ForceSetPasswordServerCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _passwordsStorage = new PasswordsStorage(_context);
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _logger = new Mock<ILogger<ForceSetPasswordServerCommandHandler>>();
    }

    private ForceSetPasswordServerCommandHandler CreateHandler()
    {
        return new ForceSetPasswordServerCommandHandler(
            _passwordsStorage, _notificationSender, _usersClient.Object, _logger.Object);
    }

    [Fact]
    public async Task Handle_SetsPasswordAndSendsNotification()
    {
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
        var cmd = new ForceSetPasswordServerCommand { UserId = 1, NewPassword = "adminpass" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var pw = await _context.UserPasswords.FirstOrDefaultAsync(x => x.UserId == 1);
        Assert.NotNull(pw);
        Assert.NotNull(pw.PasswordHash);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserContactsFailure_StillSucceeds()
    {
        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        var handler = CreateHandler();
        var cmd = new ForceSetPasswordServerCommand { UserId = 1, NewPassword = "adminpass" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
