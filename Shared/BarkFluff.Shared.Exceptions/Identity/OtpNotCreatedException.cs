namespace BarkFluff.Shared.Exceptions.Identity;

public class OtpNotCreatedException : BaseGrpcException
{
    public override string ErrorCode => "A0D92E59-DC33-4072-BFDE-12E7E26FAAD0";

    public override string ErrorMessage => "Для вызова этого метода, необходимо создать otp";
}