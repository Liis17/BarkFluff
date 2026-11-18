namespace BarkFluff.Shared.Exceptions.Identity;

public class InvalidAccountRestrictionTransitionException : BaseGrpcException
{
    public override string ErrorCode => "A58D2B61-EC71-4D62-9679-07D7C7F6D641";

    public override string ErrorMessage => "Недопустимый переход статуса аккаунта";
}
