using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Shared.Exceptions.Interceptors;

public class ServerExceptionInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (BaseGrpcException ex)
        {
            var trailers = new Metadata
            {
                { "x-error-code", ex.ErrorCode }
            };

            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.ErrorMessage), trailers);
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Unknown, "Произошла неизвестная ошибка"), ex.Message);
        }
    }
}