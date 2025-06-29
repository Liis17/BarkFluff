namespace BarkFluff.Shared.Exceptions.Navigator;

public class BeaconHostEmptyException : BaseGrpcException
{
    public override string ErrorCode => "8BD06066-81A5-43BF-84B6-A4112775E124";
    public override string ErrorMessage => "BeaconHost не может быть пустым";
} 