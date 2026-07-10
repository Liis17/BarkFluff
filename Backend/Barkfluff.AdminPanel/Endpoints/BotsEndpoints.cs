using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Bots;

using Grpc.Core;

namespace Barkfluff.AdminPanel.Endpoints;

public static class BotsEndpoints
{
    public static void MapBotsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bots")
            .WithTags("Bots");

        // GET /api/bots — все боты
        group.MapGet("/", async (
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var response = await botsClient.ListBotsAsync(new ListBotsRequest());

                var bots = response.Bots.Select(b => new
                {
                    id = b.Id,
                    username = b.Username,
                    name = b.Name,
                    ownerUserId = b.OwnerUserId,
                    systemRole = b.SystemRole,
                    createdAt = b.CreatedAt?.ToDateTime()
                });

                return Results.Ok(bots);
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("GetAllBots");

        // POST /api/bots — создать системного бота (JSON: username, name). Токен показывается один раз.
        group.MapPost("/", async (
            CreateBotRequestBody body,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Username and name are required");

            try
            {
                var response = await botsClient.CreateSystemBotAsync(new CreateSystemBotRequest
                {
                    Username = body.Username.Trim(),
                    Name = body.Name.Trim()
                });

                return Results.Ok(new { botId = response.BotId, token = response.Token });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("CreateSystemBot");

        // POST /api/bots/{id}/regenerate-token — новый токен (старый отзывается)
        group.MapPost("/{id:long}/regenerate-token", async (
            long id,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var response = await botsClient.RegenerateTokenAsync(new RegenerateTokenRequest { BotId = id });

                return Results.Ok(new { token = response.Token });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("RegenerateBotToken");

        // DELETE /api/bots/{id} — удалить бота (чаты сохраняются)
        group.MapDelete("/{id:long}", async (
            long id,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                await botsClient.DeleteBotAsync(new DeleteBotRequest { BotId = id });

                return Results.Ok();
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("DeleteBot");
    }

    public record CreateBotRequestBody(string? Username, string? Name);
}
