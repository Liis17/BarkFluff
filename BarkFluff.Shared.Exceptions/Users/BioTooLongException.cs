namespace BarkFluff.Shared.Exceptions.Users;

public class BioTooLongException : BaseGrpcException
{
    public override string ErrorCode => "1A652492-87A4-4B8B-B758-E7FBE1F39DDF";

    public override string ErrorMessage => "Био не должно превышать 200 символов";
} 