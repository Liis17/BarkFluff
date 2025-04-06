using System.Reflection;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Shared.Exceptions.Interceptors;

public class ExceptionClientInterceptor : Interceptor
{

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        try
        {
            return continuation(request, context);
        }
        catch (RpcException ex) when (ex.Trailers.Any(trailer => trailer.Key == "x-error-code"))
        {
            var errorCode = ex.Trailers.GetValue("x-error-code");
            var exceptionType = FindExceptionTypeByErrorCode(errorCode);

            if (exceptionType != null)
            {
                throw (BaseGrpcException)Activator.CreateInstance(exceptionType)!;
            }

            throw; 
        }
    }

    private Type FindExceptionTypeByErrorCode(string errorCode)
    {
        var baseType = typeof(BaseGrpcException);

        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract)
            .FirstOrDefault(t => 
                (string)t.GetField("ErrorCode", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) == errorCode);
    }
}