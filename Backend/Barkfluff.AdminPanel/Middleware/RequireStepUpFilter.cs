using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Middleware;

/// <summary>
/// Endpoint filter enforcing a Telegram step-up confirmation for critical actions.
/// Reads the confirmation id from the X-Confirmation-Id header (or the
/// "confirmation" query parameter, used by the WebSocket SSH console).
/// Responds with 428 Precondition Required and the data needed by the client
/// to request the confirmation.
/// </summary>
public class RequireStepUpFilter : IEndpointFilter
{
    public const string HeaderName = "X-Confirmation-Id";
    public const string QueryParameter = "confirmation";

    private readonly string _actionKey;
    private readonly Func<HttpContext, string> _paramsSelector;

    public RequireStepUpFilter(string actionKey, Func<HttpContext, string>? paramsSelector = null)
    {
        _actionKey = actionKey;
        _paramsSelector = paramsSelector ?? (_ => string.Empty);
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var token = http.Items["AuthToken"] as AuthToken;
        if (token == null)
            return Results.Unauthorized();

        var stepUpService = http.RequestServices.GetRequiredService<StepUpService>();
        var parameters = _paramsSelector(http);

        var confirmationId =
            http.Request.Headers[HeaderName].FirstOrDefault() ??
            http.Request.Query[QueryParameter].FirstOrDefault();

        if (!string.IsNullOrEmpty(confirmationId) &&
            stepUpService.TryConsume(confirmationId, token.Id, _actionKey, parameters))
        {
            return await next(context);
        }

        return Results.Json(
            new
            {
                error = "step_up_required",
                action = _actionKey,
                title = StepUpActions.Title(_actionKey),
                parameters
            },
            statusCode: StatusCodes.Status428PreconditionRequired);
    }
}

public static class RequireStepUpExtensions
{
    public static TBuilder RequireStepUp<TBuilder>(this TBuilder builder, string actionKey, Func<HttpContext, string>? paramsSelector = null)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(new RequireStepUpFilter(actionKey, paramsSelector));
    }
}
