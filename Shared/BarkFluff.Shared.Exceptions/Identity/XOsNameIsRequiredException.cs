namespace BarkFluff.Shared.Exceptions.Identity;

public class XOsNameIsRequiredException : BaseGrpcException
{
    public override string ErrorCode => "575EBB8D-5687-40F5-BFBB-93CD46D7564B";

    public override string ErrorMessage => "Этот запрос требует передачу x-os-name";
}