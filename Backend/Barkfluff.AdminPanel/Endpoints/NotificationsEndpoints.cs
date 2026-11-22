using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.AdminPanel.Endpoints;

public static class NotificationsEndpoints
{
    public static void MapNotificationsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequirePermission(AdminPermissions.NotificationsManage);

        // POST /api/notifications/broadcast/all
        // Body: { title, body, imageUrl?, confirm: true }
        group.MapPost("/broadcast/all", async (
            HttpRequest request,
            IPublishEndpoint publishEndpoint) =>
        {
            var body = await request.ReadFromJsonAsync<BroadcastAllBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid request body" });

            if (!body.Confirm)
                return Results.BadRequest(new { error = "Требуется подтверждение (confirm=true)" });

            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new { error = "title не может быть пустым" });

            if (string.IsNullOrWhiteSpace(body.Body))
                return Results.BadRequest(new { error = "body не может быть пустым" });

            await publishEndpoint.Publish(new AdminBroadcastNotificationEvent
            {
                Title = body.Title.Trim(),
                Body = body.Body.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(body.ImageUrl) ? null : body.ImageUrl.Trim(),
                TargetDeviceIds = []
            });

            return Results.Ok(new { enqueued = true });
        })
        .WithName("BroadcastNotificationToAll");

        // POST /api/notifications/broadcast/devices
        // Body: { title, body, imageUrl?, deviceIds: string[] }
        group.MapPost("/broadcast/devices", async (
            HttpRequest request,
            IPublishEndpoint publishEndpoint) =>
        {
            var body = await request.ReadFromJsonAsync<BroadcastDevicesBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid request body" });

            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new { error = "title не может быть пустым" });

            if (string.IsNullOrWhiteSpace(body.Body))
                return Results.BadRequest(new { error = "body не может быть пустым" });

            if (body.DeviceIds is null || body.DeviceIds.Count == 0)
                return Results.BadRequest(new { error = "Список deviceIds пуст" });

            var deviceIds = new List<Guid>(body.DeviceIds.Count);
            var invalid = new List<string>();
            foreach (var raw in body.DeviceIds)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (Guid.TryParse(raw.Trim(), out var id))
                    deviceIds.Add(id);
                else
                    invalid.Add(raw);
            }

            if (invalid.Count > 0)
                return Results.BadRequest(new { error = $"Невалидные deviceId: {string.Join(", ", invalid)}" });

            if (deviceIds.Count == 0)
                return Results.BadRequest(new { error = "Не найдено валидных deviceId" });

            await publishEndpoint.Publish(new AdminBroadcastNotificationEvent
            {
                Title = body.Title.Trim(),
                Body = body.Body.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(body.ImageUrl) ? null : body.ImageUrl.Trim(),
                TargetDeviceIds = deviceIds
            });

            return Results.Ok(new { enqueued = true, deviceCount = deviceIds.Count });
        })
        .WithName("BroadcastNotificationToDevices");

        // GET /api/notifications/test-recipient
        // → { recipient: { userId, username, firstName, lastName, devices[] } | null }
        group.MapGet("/test-recipient", (HttpContext context, NotificationTestService testService, CancellationToken cancellationToken) =>
            RunTestAsync(async () =>
            {
                var recipient = await testService.GetRecipientAsync(GetActorTelegramUserId(context), cancellationToken);
                return Results.Ok(new { recipient = recipient is null ? null : ToRecipientJson(recipient) });
            }))
        .WithName("GetTestNotificationRecipient");

        // PUT /api/notifications/test-recipient
        // Body: { barkFluffUserId }
        group.MapPut("/test-recipient", ([FromBody] TestRecipientBody? body, HttpContext context, NotificationTestService testService, CancellationToken cancellationToken) =>
            RunTestAsync(async () =>
            {
                if (body is null)
                    return Results.BadRequest(new { message = "Invalid request body" });

                var recipient = await testService.LinkRecipientAsync(
                    GetActorTelegramUserId(context),
                    GetActorName(context),
                    body.BarkFluffUserId,
                    cancellationToken);

                return Results.Ok(new { recipient = ToRecipientJson(recipient) });
            }))
        .WithName("SetTestNotificationRecipient");

        // DELETE /api/notifications/test-recipient
        group.MapDelete("/test-recipient", (HttpContext context, NotificationTestService testService) =>
            RunTestAsync(() =>
            {
                testService.UnlinkRecipient(GetActorTelegramUserId(context), GetActorName(context));
                return Task.FromResult<IResult>(Results.Ok(new { recipient = (object?)null }));
            }))
        .WithName("ClearTestNotificationRecipient");

        // POST /api/notifications/test
        // Body: { title, body, imageUrl? } — отправка только на устройства привязанного получателя
        group.MapPost("/test", ([FromBody] TestSendBody? body, HttpContext context, NotificationTestService testService, CancellationToken cancellationToken) =>
            RunTestAsync(async () =>
            {
                if (body is null)
                    return Results.BadRequest(new { message = "Invalid request body" });

                if (string.IsNullOrWhiteSpace(body.Title))
                    return Results.BadRequest(new { message = "title не может быть пустым" });

                if (string.IsNullOrWhiteSpace(body.Body))
                    return Results.BadRequest(new { message = "body не может быть пустым" });

                var devices = await testService.SendTestAsync(
                    GetActorTelegramUserId(context),
                    body.Title.Trim(),
                    body.Body.Trim(),
                    string.IsNullOrWhiteSpace(body.ImageUrl) ? null : body.ImageUrl.Trim(),
                    cancellationToken);

                return Results.Ok(new
                {
                    enqueued = true,
                    deviceCount = devices.Count,
                    devices = devices.Select(ToDeviceJson)
                });
            }))
        .WithName("SendTestNotification");
    }

    private static long GetActorTelegramUserId(HttpContext context)
    {
        // RequirePermission уже проверил токен; здесь он всегда привязан к Telegram-администратору.
        return context.GetAuthToken()?.ApprovedByTelegramUserId
            ?? throw new InvalidOperationException("Токен не связан с Telegram-администратором.");
    }

    private static string GetActorName(HttpContext context) => context.GetAuthToken()?.AdminUsername ?? "unknown";

    private static async Task<IResult> RunTestAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TestNotificationException ex)
        {
            // Поле message читает BF.request во фронтенде.
            return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static object ToRecipientJson(TestRecipient recipient) => new
    {
        userId = recipient.UserId,
        username = recipient.Username,
        firstName = recipient.FirstName,
        lastName = recipient.LastName,
        devices = recipient.Devices.Select(ToDeviceJson)
    };

    private static object ToDeviceJson(TestRecipientDevice device) => new
    {
        deviceId = device.DeviceId,
        name = device.Name,
        operationSystem = device.OperationSystem,
        appName = device.AppName,
        notificationsEnabled = device.NotificationsEnabled
    };

    private sealed record BroadcastAllBody(string? Title, string? Body, string? ImageUrl, bool Confirm);
    private sealed record BroadcastDevicesBody(string? Title, string? Body, string? ImageUrl, List<string>? DeviceIds);

    public sealed record TestRecipientBody(long BarkFluffUserId);
    public sealed record TestSendBody(string? Title, string? Body, string? ImageUrl);
}
