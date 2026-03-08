namespace BarkFluff.Shared.Exceptions.Identity;

public class XAppInfoIsRequiedException : BaseGrpcException
{
    public override string ErrorCode => "FFE79950-5668-4786-A834-6B490650FE62";

    public override string ErrorMessage => "Этот запрос требует передачи x-app-name и x-app-version";
}