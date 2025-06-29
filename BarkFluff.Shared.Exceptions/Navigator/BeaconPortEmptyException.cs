namespace BarkFluff.Shared.Exceptions.Navigator;

public class BeaconPortEmptyException : BaseGrpcException
{
    public override string ErrorCode => "F6E5D4C3-B2A1-4C5D-8B7A-9E0F1A2B3C4D";
    public override string ErrorMessage => "BeaconPort не может быть пустым";
} 