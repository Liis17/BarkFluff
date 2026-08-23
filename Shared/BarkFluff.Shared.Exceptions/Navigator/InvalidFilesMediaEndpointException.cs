namespace BarkFluff.Shared.Exceptions.Navigator;

public class InvalidFilesMediaEndpointException : BaseGrpcException
{
    public override string ErrorCode => "3B6C0D18-7E52-4A91-8C4F-2D5B9A17E604";
    public override string ErrorMessage => "FilesMediaEndpoint имеет некорректный формат (ожидается абсолютный http/https-адрес)";
}
