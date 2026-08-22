using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class StepUpEndpoints
{
    public static void MapStepUpEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/stepup")
            .WithTags("StepUp");

        // Запрос Telegram-подтверждения для критического действия
        group.MapPost("/request", async (
            StepUpRequestDto dto,
            HttpContext context,
            StepUpService stepUpService,
            IStepUpSender sender) =>
        {
            var token = context.GetAuthToken()!;
            if (token.ApprovedByTelegramUserId is not long telegramUserId)
                return Results.BadRequest(new { error = "no_telegram", message = "Сессия без Telegram-привязки не может подтверждать действия" });

            if (string.IsNullOrWhiteSpace(dto.Action))
                return Results.BadRequest(new { error = "missing_action", message = "Не указано действие" });

            if (!StepUpActions.TryGetPermission(dto.Action, out var permission))
                return Results.BadRequest(new { error = "unknown_action", message = "Неизвестное действие" });

            if (!context.HasPermission(permission))
                return Results.Forbid();

            var request = stepUpService.CreateRequest(new PendingStepUp
            {
                ActionKey = dto.Action,
                Params = dto.Parameters ?? string.Empty,
                TokenId = token.Id,
                TargetTelegramUserId = telegramUserId,
                SessionName = token.Name,
                IpAddress = context.Connection.RemoteIpAddress?.ToString()
            });

            await sender.SendStepUpRequestAsync(request);

            return Results.Ok(new { confirmationId = request.ConfirmationId, title = StepUpActions.Title(request.ActionKey) });
        })
        .WithName("CreateStepUpRequest");

        // Статус подтверждения (только для своей сессии)
        group.MapGet("/status/{confirmationId}", (
            string confirmationId,
            HttpContext context,
            StepUpService stepUpService) =>
        {
            var token = context.GetAuthToken()!;
            var request = stepUpService.GetRequest(confirmationId);
            if (request == null || request.TokenId != token.Id)
                return Results.NotFound(new { status = "expired" });

            return Results.Ok(new { status = request.Status.ToString().ToLowerInvariant() });
        })
        .WithName("GetStepUpStatus");
    }
}

public class StepUpRequestDto
{
    public string Action { get; set; } = string.Empty;
    public string? Parameters { get; set; }
}
