using System.Reflection;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Shared.Exceptions.Interceptors;

public class ExceptionClientInterceptor : Interceptor
{

    public static List<BaseGrpcException> CachedExceptions;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var call = continuation(request, context);
        
        return new AsyncUnaryCall<TResponse>(
            HandleResponse(call.ResponseAsync),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> responseTask)
    {
        try
        {
            return await responseTask.ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Trailers.Any(trailer => trailer.Key == "x-error-code"))
        {
            if (CachedExceptions is null or { Count: 0 })
            {
                CachedExceptions = LoadExceptions();
            }
            
            var errorCode = ex.Trailers.GetValue("x-error-code");
            var exception = FindExceptionByErrorCode(errorCode);

            if (exception != null)
            {
                throw exception;
            }

            throw;
        }
    }

    private List<BaseGrpcException> LoadExceptions()
    {
        var baseType = typeof(BaseGrpcException);

        var types = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);
        
        return types.Select(t => (BaseGrpcException)Activator.CreateInstance(t)!)
            .ToList();
    }

    private BaseGrpcException? FindExceptionByErrorCode(string errorCode)
    {
        return CachedExceptions.FirstOrDefault(e => e.ErrorCode == errorCode);
    }
}