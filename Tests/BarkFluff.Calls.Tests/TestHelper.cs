using System.Security.Claims;

using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Persistence;
using BarkFluff.Calls.Services;
using BarkFluff.Calls.Settings;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Calls;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Calls.Tests;

/// <summary>
/// Общие фабрики для тестов Calls: in-memory CDR, UserContext из claims, готовый
/// <see cref="CallsService"/> с реальными store/subscriptions и замоканной периферией.
/// </summary>
public static class TestHelper
{
    public static CallsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CallsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CallsContext(options);
    }

    public static UserContext CreateUserContext(long userId, string? deviceId = null)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, TokenType.User.ToString()),
        };
        if (deviceId != null)
        {
            claims.Add(new Claim(IdentityClaims.DeviceId, deviceId));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
        var httpContext = new DefaultHttpContext { User = principal };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        return new UserContext(accessor.Object);
    }

    public static CallsService CreateService(
        CallsContext db,
        long actingUserId,
        CallEventSubscriptionsManager subscriptions,
        CallQualityStore quality,
        string? deviceId = null,
        MessagesServerApi.MessagesServerApiClient? messagesClient = null)
    {
        var settings = new LiveKitSettings
        {
            Url = "ws://test:7880",
            ApiKey = "devkey",
            ApiSecret = "0123456789abcdef0123456789abcdef", // ≥32 байта — требование подписи токена
        };

        return new CallsService(
            db,
            new LiveKitTokenService(settings),
            new LocalCallEventDispatcher(subscriptions),
            quality,
            messagesClient ?? Mock.Of<MessagesServerApi.MessagesServerApiClient>(),
            Mock.Of<IPublishEndpoint>(),
            CreateUserContext(actingUserId, deviceId),
            new MetricsCollector(),
            NullLogger<CallsService>.Instance);
    }

    public static CallSession AddDirectCall(CallsContext db, long caller, long callee, CallStatus status)
    {
        var session = new CallSession
        {
            Id = Guid.NewGuid(),
            CallerUserId = caller,
            CalleeUserId = callee,
            RoomName = "call:test",
            Media = CallMediaKind.Audio,
            Status = status,
            EndReason = CallEndReasonKind.None,
            StartedAt = DateTime.UtcNow,
            AnsweredAt = status == CallStatus.Active ? DateTime.UtcNow : null,
        };
        db.CallSessions.Add(session);
        db.SaveChanges();
        return session;
    }
}

/// <summary>
/// Тестовый диспетчер: доставляет события напрямую в локальный менеджер (без RabbitMQ),
/// чтобы тесты проверяли доменную логику CallsService (кому какое событие), а не транспорт.
/// </summary>
public sealed class LocalCallEventDispatcher(CallEventSubscriptionsManager subscriptions) : ICallEventDispatcher
{
    public Task SendToUserAsync(long userId, CallEvent evt)
        => subscriptions.SendToUserAsync(userId, evt);

    public Task SendToUserExceptDeviceAsync(long userId, Guid exceptDeviceId, CallEvent evt)
        => subscriptions.SendToUserExceptDeviceAsync(userId, exceptDeviceId, evt);

    public Task SendToUsersAsync(IEnumerable<long> userIds, CallEvent evt)
        => subscriptions.SendToUsersAsync(userIds, evt);
}

/// <summary>Фейковый device-поток: вместо отправки по сети складывает события в список.</summary>
public sealed class CapturingStreamWriter : IServerStreamWriter<CallEvent>
{
    public List<CallEvent> Events { get; } = new();

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(CallEvent message)
    {
        Events.Add(message);
        return Task.CompletedTask;
    }
}
