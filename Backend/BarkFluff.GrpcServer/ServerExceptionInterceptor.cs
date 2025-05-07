using BarkFluff.Shared.Exceptions;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.GrpcServer;

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
            var baseExcetion = new BaseGrpcException();
            
            var trailers = new Metadata
            {
                { "x-error-code", baseExcetion.ErrorCode }
            };
            throw new RpcException(new Status(StatusCode.Unknown, ex.Message), trailers);
        }
    }
}