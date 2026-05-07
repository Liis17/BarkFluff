namespace BarkFluff.Shared.Exceptions.Navigator;

public class InvalidBeaconHostException : BaseGrpcException
{
    public override string ErrorCode => "B7C4D8E2-3F1A-4D6B-9C7E-2A8B5D6F1C3E";
    public override string ErrorMessage => "BeaconHost имеет некорректный формат (ожидается hostname или IP-адрес)";
}
