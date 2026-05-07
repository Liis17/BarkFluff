using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Exceptions;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.Logging;

using System.Diagnostics;

namespace BarkFluff.GrpcServer;

public class ServerExceptionInterceptor : Interceptor
{
    private readonly ILogger<ServerExceptionInterceptor> _logger;
    private readonly MetricsCollector? _metrics;

    public ServerExceptionInterceptor(ILogger<ServerExceptionInterceptor> logger, MetricsCollector? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var methodName = context.Method;
        _metrics?.Increment("grpc_requests_total");

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await continuation(request, context);
            sw.Stop();
            _metrics?.Add("grpc_request_duration_ms_total", sw.ElapsedMilliseconds);
            return response;
        }
        catch (BaseGrpcException ex)
        {
            sw.Stop();
            _metrics?.Add("grpc_request_duration_ms_total", sw.ElapsedMilliseconds);
            _metrics?.Increment("grpc_requests_failed");

            _logger.LogWarning(
                "Бизнес-ошибка при вызове {Method}: {ErrorCode} - {ErrorMessage}",
                methodName,
                ex.ErrorCode,
                ex.ErrorMessage
            );

            var trailers = new Metadata
            {
                { "x-error-code", ex.ErrorCode }
            };

            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.ErrorMessage), trailers);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _metrics?.Add("grpc_request_duration_ms_total", sw.ElapsedMilliseconds);
            _metrics?.Increment("grpc_requests_errors");

            _logger.LogError(
                ex,
                "КРИТИЧЕСКАЯ ОШИБКА при вызове {Method}. Тип: {ExceptionType}",
                methodName,
                ex.GetType().Name
            );

            var baseException = new BaseGrpcException();

            var trailers = new Metadata
            {
                { "x-error-code", baseException.ErrorCode }
            };
            throw new RpcException(new Status(StatusCode.Unknown, ex.Message), trailers);
        }
    }
}