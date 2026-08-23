namespace Barkfluff.AdminPanel.Middleware;

/// <summary>
/// Rejects sensitive admin actions without a concise audit reason.
/// Register before step-up so invalid requests are not sent to Telegram.
/// </summary>
public sealed class RequireAdminReasonFilter(
    Func<EndpointFilterInvocationContext, string?> reasonSelector) : IEndpointFilter
{
    public const int MaxLength = 500;

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var reason = reasonSelector(context)?.Trim();
        if (string.IsNullOrEmpty(reason))
            return ValueTask.FromResult<object?>(Results.BadRequest(new { message = "Укажите причину действия" }));

        if (reason.Length > MaxLength)
            return ValueTask.FromResult<object?>(Results.BadRequest(new { message = $"Причина не должна превышать {MaxLength} символов" }));

        return next(context);
    }
}

public static class RequireAdminReasonExtensions
{
    public static TBuilder RequireAdminReasonFromArguments<TBuilder>(
        this TBuilder builder,
        Func<EndpointFilterInvocationContext, string?> reasonSelector)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(new RequireAdminReasonFilter(reasonSelector));
    }
}
