using BarkFluff.Setup.Setup;

using Grpc.Core;

namespace BarkFluff.Setup.Endpoints;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/api/session", (LoginRequest? request, HttpContext http, SetupSessionStore sessions) =>
        {
            var address = ClientAddress(http);
            if (!sessions.TryLogin(request?.Token, address, out var session, out var retryAfter))
            {
                if (retryAfter > TimeSpan.Zero)
                {
                    http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                    return Results.Problem("Слишком много попыток. Повторите позже.", statusCode: StatusCodes.Status429TooManyRequests);
                }

                return Results.Problem("Неверный setup-токен.", statusCode: StatusCodes.Status401Unauthorized);
            }

            sessions.SetCookie(http, session!);
            return Results.Ok(new
            {
                csrfToken = session!.CsrfToken,
                expiresAtUtc = session.ExpiresAtUtc
            });
        });

        app.MapGet("/api/setup/state", async (HttpContext http, SetupSessionStore sessions, ISettingsSetupClient client, CancellationToken cancellationToken) =>
        {
            if (!sessions.TryGet(http, out _))
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await client.GetStateAsync(cancellationToken));
            }
            catch (RpcException exception)
            {
                return MapRpcFailure(exception);
            }
        });

        app.MapPut("/api/setup/groups/{groupId}", async (
            string groupId,
            SaveGroupRequest? request,
            HttpContext http,
            SetupSessionStore sessions,
            ISettingsSetupClient client,
            CancellationToken cancellationToken) =>
        {
            if (!sessions.TryGet(http, out var session))
                return Results.Unauthorized();
            if (!sessions.ValidateMutation(http, session))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (request?.Values is null)
                return Results.BadRequest(new { error = "Нужно передать values." });

            try
            {
                var response = await client.SaveGroupAsync(groupId, request.Values, ClientAddress(http), cancellationToken);
                return Results.Ok(response);
            }
            catch (RpcException exception)
            {
                return MapRpcFailure(exception);
            }
        });

        app.MapPost("/api/setup/complete", async (
            HttpContext http,
            SetupSessionStore sessions,
            ISettingsSetupClient client,
            CancellationToken cancellationToken) =>
        {
            if (!sessions.TryGet(http, out var session))
                return Results.Unauthorized();
            if (!sessions.ValidateMutation(http, session))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var response = await client.CompleteAsync(ClientAddress(http), cancellationToken);
                return Results.Ok(response);
            }
            catch (RpcException exception)
            {
                return MapRpcFailure(exception);
            }
        });

        app.MapDelete("/api/session", (HttpContext http, SetupSessionStore sessions) =>
        {
            sessions.Clear(http);
            return Results.NoContent();
        });

        app.MapFallbackToFile("index.html");
    }

    private static IResult MapRpcFailure(RpcException exception)
    {
        var detail = exception.StatusCode switch
        {
            StatusCode.InvalidArgument or StatusCode.FailedPrecondition => exception.Status.Detail,
            StatusCode.Unauthenticated => "Setup-сессия больше не действительна.",
            StatusCode.Unavailable or StatusCode.DeadlineExceeded => "Сервис Settings пока недоступен.",
            _ => "Не удалось выполнить операцию настройки."
        };
        var status = exception.StatusCode switch
        {
            StatusCode.InvalidArgument => StatusCodes.Status400BadRequest,
            StatusCode.Unauthenticated => StatusCodes.Status401Unauthorized,
            StatusCode.FailedPrecondition when exception.Status.Detail.Contains("already complete", StringComparison.OrdinalIgnoreCase)
                => StatusCodes.Status423Locked,
            StatusCode.FailedPrecondition => StatusCodes.Status409Conflict,
            StatusCode.Unavailable or StatusCode.DeadlineExceeded => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };
        return Results.Problem(detail, statusCode: status);
    }

    private static string ClientAddress(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public sealed record LoginRequest(string? Token);

    public sealed record SaveGroupRequest(Dictionary<string, string?> Values);
}
