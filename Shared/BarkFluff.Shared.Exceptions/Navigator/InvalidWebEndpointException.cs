namespace BarkFluff.Shared.Exceptions.Navigator;

public class InvalidWebEndpointException : BaseGrpcException
{
    public override string ErrorCode => "5D9E1F42-8A3C-4B7E-9F26-C1D0A4B85E37";
    public override string ErrorMessage => "WebEndpoint имеет некорректный формат (ожидается абсолютный http/https-адрес)";
}
