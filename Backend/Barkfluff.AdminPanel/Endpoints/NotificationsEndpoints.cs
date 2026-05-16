using Barkfluff.AdminPanel.Models;

using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace Barkfluff.AdminPanel.Endpoints;

public static class NotificationsEndpoints
{
    public static void MapNotificationsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications");

        // POST /api/notifications/broadcast/all
        // Body: { title, body, imageUrl?, confirm: true }
        group.MapPost("/broadcast/all", async (
            HttpRequest request,
            IPublishEndpoint publishEndpoint,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

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
            IPublishEndpoint publishEndpoint,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

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
    }

    private sealed record BroadcastAllBody(string? Title, string? Body, string? ImageUrl, bool Confirm);
    private sealed record BroadcastDevicesBody(string? Title, string? Body, string? ImageUrl, List<string>? DeviceIds);
}
