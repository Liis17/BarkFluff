namespace BarkFluff.Shared.Exceptions.Identity;

public class XDeviceNameIsRequiredException : BaseGrpcException
{
    public override string ErrorCode => "4E98408C-C969-4737-936B-A2AABB05B88D";

    public override string ErrorMessage => "Этот запрос требует передачу x-device-name";
}