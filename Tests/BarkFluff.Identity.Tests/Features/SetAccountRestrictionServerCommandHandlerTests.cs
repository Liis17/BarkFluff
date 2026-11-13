using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.SetAccountRestrictionServer;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Identity;
using BarkFluff.Shared.Queue.Notifications;
using BarkFluff.Shared.Queue.Users;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class SetAccountRestrictionServerCommandHandlerTests
{
    [Fact]
    public async Task Handle_BlockPublishesRevocationForTokensAndDevicesBeforeDeletingTokens()
    {
        await using var context = TestHelper.CreateContext();
        context.RefreshTokens.AddRange(
            new RefreshToken { UserId = 42, DeviceId = "token-device", Value = "refresh-1", ExpiresAt = DateTime.UtcNow.AddDays(1) },
            new RefreshToken { UserId = 42, DeviceId = "users-device", Value = "refresh-2", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await context.SaveChangesAsync();

        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.SetAccountRestrictionAsync(It.IsAny<SetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Unary(new SetAccountRestrictionResponse
            {
                User = new User { Id = 42, Username = "raw-user" },
                Email = "raw@example.com",
                Restriction = new AccountRestrictionInfo { Status = AccountStatus.Blocked, Reason = "policy" },
            }));
        users.Setup(client => client.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Unary(new GetUserDevicesResponse
            {
                Devices = { new Device { UserId = 42, DeviceId = "users-device" }, new Device { UserId = 42, DeviceId = "registered-only" } }
            }));
        users.Setup(client => client.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Unary(new DeleteUserDeviceResponse()));

        var revokedDevices = new List<string>();
        var tokensPresentDuringPublish = false;
        var publisher = new Mock<IPublishEndpoint>();
        publisher.Setup(endpoint => endpoint.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SessionRevokedEvent, CancellationToken>((message, _) =>
            {
                revokedDevices.Add(message.DeviceId);
                tokensPresentDuringPublish |= context.RefreshTokens.Any();
            }).Returns(Task.CompletedTask);
        publisher.Setup(endpoint => endpoint.Publish(It.IsAny<UserAccountStatusChanged>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(endpoint => endpoint.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new SetAccountRestrictionServerCommandHandler(
            users.Object, context, new RefreshTokensStorage(context), new NotificationQueueSender(publisher.Object),
            publisher.Object, new JwtSettings { ExpiryMinutes = 60 }, NullLogger<SetAccountRestrictionServerCommandHandler>.Instance);

        var result = await handler.Handle(new SetAccountRestrictionServerCommand
        {
            UserId = 42,
            Action = AccountRestrictionAction.Block,
            Reason = "policy",
        }, CancellationToken.None);

        Assert.Equal(AccountStatus.Blocked, result.Restriction.Status);
        Assert.Equal(new[] { "registered-only", "token-device", "users-device" }, revokedDevices.Order());
        Assert.True(tokensPresentDuringPublish);
        Assert.Empty(context.RefreshTokens);
        users.Verify(client => client.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_RevocationPublishFailureLeavesRefreshTokensForRetry()
    {
        await using var context = TestHelper.CreateContext();
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = 42, DeviceId = "device", Value = "refresh", ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await context.SaveChangesAsync();

        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.SetAccountRestrictionAsync(It.IsAny<SetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Unary(new SetAccountRestrictionResponse
            {
                User = new User { Id = 42, Username = "raw-user" },
                Restriction = new AccountRestrictionInfo { Status = AccountStatus.Blocked, Reason = "policy" },
            }));
        users.Setup(client => client.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Unary(new GetUserDevicesResponse()));

        var publisher = new Mock<IPublishEndpoint>();
        publisher.Setup(endpoint => endpoint.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(new InvalidOperationException("broker unavailable")));
        var handler = new SetAccountRestrictionServerCommandHandler(
            users.Object, context, new RefreshTokensStorage(context), new NotificationQueueSender(publisher.Object),
            publisher.Object, new JwtSettings(), NullLogger<SetAccountRestrictionServerCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new SetAccountRestrictionServerCommand
        {
            UserId = 42,
            Action = AccountRestrictionAction.Block,
            Reason = "policy",
        }, CancellationToken.None));

        Assert.Single(context.RefreshTokens);
        users.Verify(client => client.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CancelDeletionRestoresBlockedStatusAndExplainsItInEmail()
    {
        await using var context = TestHelper.CreateContext();
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = 42, DeviceId = "device", Value = "refresh", ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await context.SaveChangesAsync();

        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.SetAccountRestrictionAsync(It.IsAny<SetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Unary(new SetAccountRestrictionResponse
            {
                User = new User { Id = 42, Username = "raw-user" },
                Email = "user@example.com",
                Restriction = new AccountRestrictionInfo { Status = AccountStatus.Blocked, Reason = "manual review" },
            }));
        var notifications = new List<EmailNotification>();
        var accountStatusEvents = new List<UserAccountStatusChanged>();
        var publisher = new Mock<IPublishEndpoint>();
        publisher.Setup(endpoint => endpoint.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()))
            .Callback<EmailNotification, CancellationToken>((message, _) => notifications.Add(message)).Returns(Task.CompletedTask);
        publisher.Setup(endpoint => endpoint.Publish(It.IsAny<UserAccountStatusChanged>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccountStatusChanged, CancellationToken>((message, _) => accountStatusEvents.Add(message)).Returns(Task.CompletedTask);

        var handler = new SetAccountRestrictionServerCommandHandler(
            users.Object, context, new RefreshTokensStorage(context), new NotificationQueueSender(publisher.Object),
            publisher.Object, new JwtSettings(), NullLogger<SetAccountRestrictionServerCommandHandler>.Instance);

        var result = await handler.Handle(new SetAccountRestrictionServerCommand
        {
            UserId = 42,
            Action = AccountRestrictionAction.CancelDeletion,
            Reason = "cancel request",
        }, CancellationToken.None);

        Assert.Equal(AccountStatus.Blocked, result.Restriction.Status);
        Assert.Equal("manual review", result.Restriction.Reason);
        Assert.Empty(context.RefreshTokens);
        Assert.Equal((int)AccountStatus.Blocked, Assert.Single(accountStatusEvents).Status);
        var email = Assert.Single(notifications);
        Assert.Equal(NotificationType.AccountDeletionCancelled, email.Type);
        Assert.Contains("Аккаунт остаётся заблокированным", email.Payload["statusdescription"]);
        Assert.Contains("manual review", email.Payload["statusdescription"]);
        publisher.Verify(endpoint => endpoint.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AsyncUnaryCall<T> Unary<T>(T response) => new(
        Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
