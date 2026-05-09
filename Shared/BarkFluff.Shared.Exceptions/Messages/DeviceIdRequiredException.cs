namespace BarkFluff.Shared.Exceptions.Messages;

public class DeviceIdRequiredException : BaseGrpcException
{
    public override string ErrorCode => "8E0F3D90-6F4B-4D4D-B5C8-3F6E1D2A0B43";

    public override string ErrorMessage => "Операция требует device_id в токене";
}
