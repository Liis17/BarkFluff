using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Federation;

public class InvalidServernameException : BaseGrpcException
{
    public override string ErrorCode => "BD255D17-12CB-4D02-A26A-43F3E876C7D9";
    public override string ErrorMessage => "Некорректный формат server_name";
    public override StatusCode StatusCode => StatusCode.InvalidArgument;
}
