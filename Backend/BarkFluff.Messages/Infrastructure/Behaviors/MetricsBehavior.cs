using System.Diagnostics;
using System.Text;

using BarkFluff.GrpcServer.Metrics;

using MediatR;

namespace BarkFluff.Messages.Infrastructure.Behaviors;

/// <summary>
/// Pipeline behavior, который автоматически записывает counters/duration по каждой MediatR-операции.
/// Имя метрики формируется из имени запроса: ListChatsCommand → list_chats.
/// Метрики: {op}_requests, {op}_success, {op}_errors, {op}_duration_ms_total.
/// </summary>
public class MetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly MetricsCollector _metrics;

    public MetricsBehavior(MetricsCollector metrics)
    {
        _metrics = metrics;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var op = GetOperationName(typeof(TRequest).Name);
        _metrics.Increment($"{op}_requests");

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            sw.Stop();
            _metrics.Increment($"{op}_success");
            _metrics.Add($"{op}_duration_ms_total", sw.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment($"{op}_errors");
            _metrics.Add($"{op}_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }

    private static string GetOperationName(string typeName)
    {
        var name = typeName;
        if (name.EndsWith("CommandHandler")) name = name[..^"CommandHandler".Length];
        if (name.EndsWith("QueryHandler")) name = name[..^"QueryHandler".Length];
        if (name.EndsWith("Command")) name = name[..^"Command".Length];
        if (name.EndsWith("Query")) name = name[..^"Query".Length];
        return ToSnakeCase(name);
    }

    private static string ToSnakeCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (char.IsUpper(ch))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
