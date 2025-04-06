using BarkFluff.Proto.Files;
using Grpc.Core;

namespace BarkFluff.Files.Host;

public class FilesApiService : BarkFluff.Proto.Files.FilesApi.FilesApiBase
{
    public override Task<HelloResponse> SayHello(HelloRequest request, ServerCallContext context)
    {
        return base.SayHello(request, context);
    }
}